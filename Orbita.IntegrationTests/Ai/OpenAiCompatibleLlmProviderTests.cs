using System.Net;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.Infrastructure.Ai;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// Exercises the OpenAI-compatible adapter against the JSON that shape documents. This
/// is the adapter that makes migrating providers a configuration change, so the tests
/// pin the wire format rather than any one vendor's behaviour.
/// </summary>
public sealed class OpenAiCompatibleLlmProviderTests
{
    private static readonly LlmCompletionRequest Request = new(
        Guid.NewGuid(),
        LlmTask.Draft,
        [LlmMessage.User("hola")],
        Temperature: 0.3m,
        MaxTokens: 800);

    private static OpenAiCompatibleLlmProvider Build(
        StubHttpMessageHandler handler,
        decimal inputPrice = 0m,
        decimal outputPrice = 0m,
        string chatModel = "local-model")
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234/") },
            new StubModelSelector(chatModel, "text-embedding-3-small"),
            new StubLlmPricing(inputPrice, outputPrice));

    [Fact]
    public async Task CompleteAsync_reads_content_and_token_counts()
    {
        var handler = StubHttpMessageHandler.RespondingWith("""
            {
              "model": "local-model",
              "choices": [
                { "message": { "role": "assistant", "content": "¡Hola!" }, "finish_reason": "stop" }
              ],
              "usage": { "prompt_tokens": 20, "completion_tokens": 4 }
            }
            """);

        var result = await Build(handler).CompleteAsync(Request, CancellationToken.None);

        Assert.Equal("¡Hola!", result.Content);
        Assert.Equal(20, result.Usage.TokensIn);
        Assert.Equal(4, result.Usage.TokensOut);
        Assert.Equal("stop", result.Usage.FinishReason);
        Assert.Contains("v1/chat/completions", handler.ReceivedUris[0].AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cost_is_computed_from_the_configured_per_million_token_prices()
    {
        // Zero by default because the default target runs locally — but the moment a
        // paid endpoint is configured, ai_runs has to reflect real spend.
        var handler = StubHttpMessageHandler.RespondingWith("""
            {
              "model": "local-model",
              "choices": [{ "message": { "content": "ok" }, "finish_reason": "stop" }],
              "usage": { "prompt_tokens": 1000000, "completion_tokens": 500000 }
            }
            """);

        var result = await Build(handler, inputPrice: 0.15m, outputPrice: 0.60m)
            .CompleteAsync(Request, CancellationToken.None);

        Assert.Equal(0.45m, result.Usage.CostUsd);
    }

    [Fact]
    public async Task Cost_is_zero_when_no_price_is_configured()
    {
        var handler = StubHttpMessageHandler.RespondingWith("""
            {
              "choices": [{ "message": { "content": "ok" }, "finish_reason": "stop" }],
              "usage": { "prompt_tokens": 900, "completion_tokens": 100 }
            }
            """);

        var result = await Build(handler).CompleteAsync(Request, CancellationToken.None);

        Assert.Equal(0m, result.Usage.CostUsd);
    }

    [Fact]
    public async Task CompleteAsync_keeps_tool_arguments_as_the_json_string_the_api_sends()
    {
        var handler = StubHttpMessageHandler.RespondingWith("""
            {
              "choices": [{
                "message": {
                  "content": null,
                  "tool_calls": [{
                    "id": "call_abc",
                    "type": "function",
                    "function": { "name": "agendar_cita", "arguments": "{\"fecha\":\"2026-09-10\"}" }
                  }]
                },
                "finish_reason": "tool_calls"
              }]
            }
            """);

        var result = await Build(handler).CompleteAsync(Request, CancellationToken.None);

        var toolCall = Assert.Single(result.ToolCalls);
        Assert.Equal("call_abc", toolCall.Id);
        Assert.Equal("agendar_cita", toolCall.Name);
        Assert.Equal("""{"fecha":"2026-09-10"}""", toolCall.ArgumentsJson);
    }

    [Fact]
    public async Task A_tool_result_message_carries_the_id_of_the_call_it_answers()
    {
        var handler = StubHttpMessageHandler.RespondingWith("""
            { "choices": [{ "message": { "content": "listo" }, "finish_reason": "stop" }] }
            """);

        var request = Request with
        {
            Messages = [LlmMessage.User("agenda"), LlmMessage.Tool("call_abc", """{"ok":true}""")],
        };

        await Build(handler).CompleteAsync(request, CancellationToken.None);

        Assert.Contains("\"tool_call_id\":\"call_abc\"", Assert.Single(handler.ReceivedBodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_reassembles_sse_deltas_and_ends_with_usage()
    {
        var handler = StubHttpMessageHandler.RespondingWithSse(
            """{"model":"local-model","choices":[{"delta":{"content":"Hola"}}]}""",
            """{"model":"local-model","choices":[{"delta":{"content":" mundo"}}]}""",
            """{"model":"local-model","choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":2}}""",
            "[DONE]");

        var chunks = new List<LlmChunk>();
        await foreach (var chunk in Build(handler).StreamAsync(Request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Equal("Hola mundo", string.Concat(chunks.Select(chunk => chunk.DeltaContent)));
        Assert.True(chunks[^1].IsFinal);
        Assert.Equal(5, chunks[^1].Usage!.TokensIn);
        Assert.Equal("stop", chunks[^1].Usage!.FinishReason);
    }

    [Fact]
    public async Task StreamAsync_only_surfaces_a_tool_call_once_its_arguments_are_complete()
    {
        // Arguments arrive in fragments; a consumer must never see half-written JSON.
        var handler = StubHttpMessageHandler.RespondingWithSse(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"mover_etapa","arguments":"{\"etapa\""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":":\"ganado\"}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            "[DONE]");

        var toolCalls = new List<LlmToolCall>();
        await foreach (var chunk in Build(handler).StreamAsync(Request, CancellationToken.None))
        {
            if (chunk.ToolCall is { } toolCall)
            {
                toolCalls.Add(toolCall);
            }
        }

        var call = Assert.Single(toolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("mover_etapa", call.Name);
        Assert.Equal("""{"etapa":"ganado"}""", call.ArgumentsJson);
    }

    [Fact]
    public async Task Rate_limiting_is_transient_so_it_is_retried_rather_than_surfaced()
    {
        var handler = StubHttpMessageHandler.RespondingWith("slow down", HttpStatusCode.TooManyRequests);

        var failure = await Assert.ThrowsAsync<LlmProviderException>(
            () => Build(handler).CompleteAsync(Request, CancellationToken.None));

        Assert.True(failure.IsTransient);
        Assert.Equal("openai-compatible", failure.ProviderName);
    }

    [Fact]
    public async Task A_rejected_request_is_permanent()
    {
        var handler = StubHttpMessageHandler.RespondingWith("""{"error":"context length exceeded"}""", HttpStatusCode.BadRequest);

        var failure = await Assert.ThrowsAsync<LlmProviderException>(
            () => Build(handler).CompleteAsync(Request, CancellationToken.None));

        Assert.False(failure.IsTransient);
    }

    [Fact]
    public async Task EmbedAsync_reads_the_first_vector_from_the_data_array()
    {
        var handler = StubHttpMessageHandler.RespondingWith("""
            {
              "model": "text-embedding-3-small",
              "data": [{ "embedding": [0.4, 0.5] }],
              "usage": { "prompt_tokens": 7, "completion_tokens": 0 }
            }
            """);

        var result = await Build(handler).EmbedAsync("hola", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal([0.4f, 0.5f], result.Vector);
        Assert.Equal(7, result.Usage.TokensIn);
        Assert.Contains("v1/embeddings", handler.ReceivedUris[0].AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void IsConfigured_is_false_without_a_base_address()
        => Assert.False(
            new OpenAiCompatibleLlmProvider(
                new HttpClient(),
                new StubModelSelector("local-model", "text-embedding-3-small"),
                new StubLlmPricing(0m, 0m)).IsConfigured);
}
