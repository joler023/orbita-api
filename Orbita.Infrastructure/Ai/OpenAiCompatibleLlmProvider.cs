using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// ORB-C01's second implementation, and the one that makes "no dependemos de un solo
/// proveedor" true in practice: it speaks the OpenAI chat-completions shape
/// (<c>/v1/chat/completions</c> + <c>/v1/embeddings</c>), which is the de-facto standard
/// that LM Studio, llama.cpp, vLLM, Groq, OpenRouter, Together and OpenAI itself all
/// serve.
///
/// That is why it is one adapter rather than five: today it points at a local server and
/// costs nothing, and moving to a paid provider is a change to
/// <c>Ai:Providers:OpenAiCompatible:BaseUrl</c> and <c>ApiKey</c> — configuration, not
/// code. It follows the posture ORB-A12 already established for Stripe and Wompi: fully
/// implemented, credentials empty by default, inert until someone fills them in.
///
/// One gateway commonly serves many models at different rates (OpenRouter fronts
/// <c>openai/gpt-5.6-luna</c>, <c>deepseek/deepseek-v4-flash</c> and
/// <c>google/gemini-3.5-flash-lite</c> at three different prices), so cost comes from
/// <see cref="ILlmPricing"/> keyed by the resolved model — not from a single rate
/// attached to the provider.
/// </summary>
public sealed class OpenAiCompatibleLlmProvider(
    HttpClient httpClient,
    ILlmModelSelector modelSelector,
    ILlmPricing pricing) : ILlmProvider
{
    private const string ChatPath = "v1/chat/completions";
    private const string EmbeddingsPath = "v1/embeddings";
    private const string DataPrefix = "data:";
    private const string DoneSentinel = "[DONE]";

    public string Name => "openai-compatible";

    public bool IsConfigured => httpClient.BaseAddress is not null;

    public async Task<LlmCompletionResult> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken)
    {
        var model = await modelSelector.SelectModelAsync(request.TenantId, request.Task, Name, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(ChatPath, BuildChatRequest(request, model, stream: false), cancellationToken);
        var payload = await ReadAsync<OpenAiChatResponse>(response, cancellationToken);
        stopwatch.Stop();

        var choice = payload.Choices?.FirstOrDefault()
            ?? throw LlmHttpFailure.FromMalformedResponse(Name, "the choices array was empty");

        var toolCalls = (choice.Message?.ToolCalls ?? [])
            .Where(call => call.Function?.Name is not null)
            .Select((call, index) => new LlmToolCall(
                call.Id ?? $"call-{index}",
                call.Function!.Name!,
                call.Function.Arguments ?? "{}"))
            .ToList();

        return new LlmCompletionResult(
            choice.Message?.Content,
            toolCalls,
            BuildUsage(payload.Model ?? model, payload.Usage, stopwatch, choice.FinishReason));
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resolvedModel = await modelSelector.SelectModelAsync(request.TenantId, request.Task, Name, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(ChatPath, BuildChatRequest(request, resolvedModel, stream: true), cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // Tool calls arrive as fragments spread over several frames, keyed by index —
        // they are accumulated here and only surfaced once the stream says generation
        // finished, so a consumer never sees half-written arguments.
        var pendingToolCalls = new SortedDictionary<int, PendingToolCall>();
        var model = resolvedModel;
        OpenAiUsage? usage = null;
        string? finishReason = null;

        while (true)
        {
            string? line;

            // No `yield` inside this try — C# forbids yielding from a try with a catch.
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException)
            {
                throw LlmHttpFailure.FromTransport(Name, exception);
            }

            if (line is null)
            {
                break;
            }

            if (!line.StartsWith(DataPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[DataPrefix.Length..].Trim();

            if (data.Length == 0)
            {
                continue;
            }

            if (data == DoneSentinel)
            {
                break;
            }

            var frame = Deserialize<OpenAiChatResponse>(data);
            model = frame.Model ?? model;
            usage = frame.Usage ?? usage;

            var choice = frame.Choices?.FirstOrDefault();
            if (choice is null)
            {
                continue;
            }

            finishReason = choice.FinishReason ?? finishReason;

            foreach (var fragment in choice.Delta?.ToolCalls ?? [])
            {
                Accumulate(pendingToolCalls, fragment);
            }

            if (choice.Delta?.Content is { Length: > 0 } content)
            {
                yield return new LlmChunk(content, null, IsFinal: false, Usage: null);
            }
        }

        foreach (var pending in pendingToolCalls.Values)
        {
            yield return new LlmChunk(null, pending.ToToolCall(), IsFinal: false, Usage: null);
        }

        stopwatch.Stop();
        yield return new LlmChunk(
            null,
            null,
            IsFinal: true,
            BuildUsage(model, usage, stopwatch, finishReason));
    }

    public async Task<LlmEmbeddingResult> EmbedAsync(string text, Guid tenantId, CancellationToken cancellationToken)
    {
        var model = await modelSelector.SelectModelAsync(tenantId, LlmTask.Embed, Name, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(
            EmbeddingsPath,
            new OpenAiEmbeddingRequest { Model = model, Input = text },
            cancellationToken);
        var payload = await ReadAsync<OpenAiEmbeddingResponse>(response, cancellationToken);
        stopwatch.Stop();

        if (payload.Data?.FirstOrDefault()?.Embedding is not { } vector)
        {
            throw LlmHttpFailure.FromMalformedResponse(Name, "the data array carried no embedding");
        }

        return new LlmEmbeddingResult(
            vector,
            BuildUsage(payload.Model ?? model, payload.Usage, stopwatch, finishReason: null));
    }

    private static void Accumulate(SortedDictionary<int, PendingToolCall> pending, OpenAiResponseToolCall fragment)
    {
        var index = fragment.Index ?? 0;

        if (!pending.TryGetValue(index, out var call))
        {
            call = new PendingToolCall();
            pending[index] = call;
        }

        call.Id ??= fragment.Id;
        call.Name ??= fragment.Function?.Name;

        if (fragment.Function?.Arguments is { Length: > 0 } argumentsFragment)
        {
            call.Arguments.Append(argumentsFragment);
        }
    }

    private static OpenAiChatRequest BuildChatRequest(LlmCompletionRequest request, string model, bool stream)
        => new()
        {
            Model = model,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            Stream = stream,
            StreamOptions = stream ? new OpenAiStreamOptions() : null,
            Messages = request.Messages
                .Select(message => new OpenAiRequestMessage
                {
                    Role = RoleName(message.Role),
                    Content = message.Content,
                    ToolCallId = message.ToolCallId,
                    ToolCalls = message.ToolCalls is { Count: > 0 } calls
                        ? calls.Select(call => new OpenAiRequestToolCall
                        {
                            Id = call.Id,
                            Function = new OpenAiRequestToolCallFunction { Name = call.Name, Arguments = call.ArgumentsJson },
                        }).ToList()
                        : null,
                })
                .ToList(),
            Tools = request.Tools is { Count: > 0 } tools
                ? tools.Select(tool => new OpenAiTool
                {
                    Function = new OpenAiToolFunction
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        Parameters = ParseSchema(tool),
                    },
                }).ToList()
                : null,
        };

    private static JsonElement ParseSchema(LlmTool tool)
    {
        try
        {
            return JsonDocument.Parse(tool.ParametersJsonSchema).RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                $"Tool '{tool.Name}' has a ParametersJsonSchema that is not valid JSON.", nameof(tool), exception);
        }
    }

    private static string RoleName(LlmMessageRole role) => role switch
    {
        LlmMessageRole.System => "system",
        LlmMessageRole.User => "user",
        LlmMessageRole.Assistant => "assistant",
        LlmMessageRole.Tool => "tool",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unmapped message role."),
    };

    private LlmUsage BuildUsage(string model, OpenAiUsage? usage, Stopwatch stopwatch, string? finishReason)
    {
        var tokensIn = usage?.PromptTokens ?? 0;
        var tokensOut = usage?.CompletionTokens ?? 0;

        return new LlmUsage(
            model,
            tokensIn,
            tokensOut,
            pricing.CostFor(model, tokensIn, tokensOut),
            (int)stopwatch.ElapsedMilliseconds,
            finishReason);
    }

    private async Task<HttpResponseMessage> SendAsync<TRequest>(string path, TRequest body, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.PostAsJsonAsync(path, body, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw LlmHttpFailure.FromTransport(Name, exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw LlmHttpFailure.FromStatus(Name, response.StatusCode, errorBody);
        }

        return response;
    }

    private async Task<TResponse> ReadAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
        => Deserialize<TResponse>(await response.Content.ReadAsStringAsync(cancellationToken));

    private TResponse Deserialize<TResponse>(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<TResponse>(raw)
                ?? throw LlmHttpFailure.FromMalformedResponse(Name, "the body deserialized to null");
        }
        catch (JsonException exception)
        {
            throw LlmHttpFailure.FromMalformedResponse(Name, exception.Message);
        }
    }

    private sealed class PendingToolCall
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public System.Text.StringBuilder Arguments { get; } = new();

        public LlmToolCall ToToolCall()
            => new(Id ?? "call-0", Name ?? "unknown", Arguments.Length == 0 ? "{}" : Arguments.ToString());
    }
}
