using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Ai;

/// <summary>
/// ORB-C01's locally hosted provider: Ollama, talking its native API on
/// <c>http://localhost:11434</c>. It is the default because it costs nothing, needs no
/// account and no API key, and runs offline — the whole knowledge-base pipeline
/// (ORB-C02/C03) can be developed and tested without anyone's billing details.
///
/// Because the model runs on the developer's own machine, <see cref="LlmUsage.CostUsd"/>
/// is always <c>0</c>. That is a real measurement, not a missing one.
///
/// Two shape differences from OpenAI-style APIs are absorbed here so callers never see
/// them: Ollama returns tool arguments as a JSON object rather than a string, and it
/// does not issue tool-call ids, so this adapter synthesizes them.
/// </summary>
public sealed class OllamaLlmProvider(HttpClient httpClient, ILlmModelSelector modelSelector) : ILlmProvider
{
    private const string ChatPath = "/api/chat";
    private const string EmbedPath = "/api/embed";

    public string Name => "ollama";

    public bool IsConfigured => httpClient.BaseAddress is not null;

    public async Task<LlmCompletionResult> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken)
    {
        var model = await modelSelector.SelectModelAsync(request.TenantId, request.Task, Name, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(ChatPath, BuildChatRequest(request, model, stream: false), cancellationToken);
        var payload = await ReadAsync<OllamaChatResponse>(response, cancellationToken);
        stopwatch.Stop();

        var toolCalls = MapToolCalls(payload.Message?.ToolCalls);

        return new LlmCompletionResult(
            payload.Message?.Content,
            toolCalls,
            new LlmUsage(
                payload.Model ?? model,
                payload.PromptEvalCount ?? 0,
                payload.EvalCount ?? 0,
                CostUsd: 0m,
                (int)stopwatch.ElapsedMilliseconds,
                payload.DoneReason ?? (toolCalls.Count > 0 ? "tool_calls" : "stop")));
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var model = await modelSelector.SelectModelAsync(request.TenantId, request.Task, Name, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(ChatPath, BuildChatRequest(request, model, stream: true), cancellationToken);

        // Ollama streams newline-delimited JSON: one complete object per line, the last
        // one carrying done=true plus the token counts.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

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
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var payload = Deserialize<OllamaChatResponse>(line);

            foreach (var toolCall in MapToolCalls(payload.Message?.ToolCalls))
            {
                yield return new LlmChunk(null, toolCall, IsFinal: false, Usage: null);
            }

            if (payload.Done)
            {
                stopwatch.Stop();
                yield return new LlmChunk(
                    null,
                    null,
                    IsFinal: true,
                    new LlmUsage(
                        payload.Model ?? model,
                        payload.PromptEvalCount ?? 0,
                        payload.EvalCount ?? 0,
                        CostUsd: 0m,
                        (int)stopwatch.ElapsedMilliseconds,
                        payload.DoneReason ?? "stop"));
                yield break;
            }

            if (payload.Message?.Content is { Length: > 0 } content)
            {
                yield return new LlmChunk(content, null, IsFinal: false, Usage: null);
            }
        }
    }

    public async Task<LlmEmbeddingResult> EmbedAsync(string text, Guid tenantId, CancellationToken cancellationToken)
    {
        var model = await modelSelector.SelectModelAsync(tenantId, LlmTask.Embed, Name, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(EmbedPath, new OllamaEmbedRequest { Model = model, Input = text }, cancellationToken);
        var payload = await ReadAsync<OllamaEmbedResponse>(response, cancellationToken);
        stopwatch.Stop();

        if (payload.Embeddings is not [var vector, ..])
        {
            throw LlmHttpFailure.FromMalformedResponse(Name, "the embeddings array was empty");
        }

        return new LlmEmbeddingResult(
            vector,
            new LlmUsage(
                payload.Model ?? model,
                payload.PromptEvalCount ?? 0,
                TokensOut: 0,
                CostUsd: 0m,
                (int)stopwatch.ElapsedMilliseconds,
                FinishReason: null));
    }

    /// <summary>
    /// Ollama's <c>/api/embed</c> does take an array, but this adapter has never been run
    /// against a configured instance (<c>BaseUrl</c> is empty in every environment today),
    /// so it embeds one at a time and adds the cost up rather than shipping an untested
    /// wire shape. The contract callers see is identical; only the round trips differ, and
    /// a local Ollama has no network latency to save.
    /// </summary>
    public async Task<LlmEmbeddingBatchResult> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            throw new ArgumentException("There is nothing to embed.", nameof(texts));
        }

        var vectors = new List<IReadOnlyList<float>>(texts.Count);
        var tokensIn = 0;
        var latencyMs = 0;
        var model = string.Empty;

        foreach (var text in texts)
        {
            var single = await EmbedAsync(text, tenantId, cancellationToken);

            vectors.Add(single.Vector);
            tokensIn += single.Usage.TokensIn;
            latencyMs += single.Usage.LatencyMs;
            model = single.Usage.Model;
        }

        return new LlmEmbeddingBatchResult(
            vectors,
            new LlmUsage(model, tokensIn, TokensOut: 0, CostUsd: 0m, latencyMs, FinishReason: null));
    }

    private static OllamaChatRequest BuildChatRequest(LlmCompletionRequest request, string model, bool stream)
        => new()
        {
            Model = model,
            Stream = stream,
            Options = new OllamaOptions { Temperature = request.Temperature, NumPredict = request.MaxTokens },
            Messages = request.Messages
                .Select(message => new OllamaRequestMessage { Role = RoleName(message.Role), Content = message.Content })
                .ToList(),
            Tools = request.Tools is { Count: > 0 } tools
                ? tools.Select(tool => new OllamaTool
                {
                    Function = new OllamaToolFunction
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

    private static IReadOnlyList<LlmToolCall> MapToolCalls(IReadOnlyList<OllamaResponseToolCall>? toolCalls)
    {
        if (toolCalls is null or { Count: 0 })
        {
            return [];
        }

        return toolCalls
            .Where(call => call.Function?.Name is not null)
            .Select((call, index) => new LlmToolCall(
                // Ollama issues no ids; a positional one is enough for the caller to
                // pair a result back to its call within a single turn.
                $"ollama-{index}",
                call.Function!.Name!,
                call.Function.Arguments.ValueKind is JsonValueKind.Undefined
                    ? "{}"
                    : call.Function.Arguments.GetRawText()))
            .ToList();
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
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return Deserialize<TResponse>(raw);
    }

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
}
