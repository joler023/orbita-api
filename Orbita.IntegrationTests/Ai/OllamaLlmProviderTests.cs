using System.Net;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.Infrastructure.Ai;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// Exercises the Ollama adapter against the exact JSON Ollama's API documents. Lives
/// here rather than in Orbita.UnitTests because only this project references
/// Orbita.Infrastructure — the same reason StripePaymentProviderTests does. No Docker
/// and no running Ollama are involved.
/// </summary>
public sealed class OllamaLlmProviderTests
{
    private static readonly LlmCompletionRequest Request = new(
        Guid.NewGuid(),
        LlmTask.Draft,
        [LlmMessage.System("eres un asistente"), LlmMessage.User("hola")],
        Temperature: 0.3m,
        MaxTokens: 800);

    private static OllamaLlmProvider Build(StubHttpMessageHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") },
            new StubModelSelector("llama3.1", "nomic-embed-text"));

    [Fact]
    public async Task CompleteAsync_reads_content_and_token_counts()
    {
        var handler = StubHttpMessageHandler.RespondingWith("""
            {
              "model": "llama3.1",
              "message": { "role": "assistant", "content": "¡Hola! ¿En qué te ayudo?" },
              "done": true,
              "done_reason": "stop",
              "prompt_eval_count": 26,
              "eval_count": 12
            }
            """);

        var result = await Build(handler).CompleteAsync(Request, CancellationToken.None);

        Assert.Equal("¡Hola! ¿En qué te ayudo?", result.Content);
        Assert.Equal(26, result.Usage.TokensIn);
        Assert.Equal(12, result.Usage.TokensOut);
        Assert.Equal("stop", result.Usage.FinishReason);
        // A local model has no bill attached; zero here is a measurement, not a gap.
        Assert.Equal(0m, result.Usage.CostUsd);
    }

    [Fact]
    public async Task CompleteAsync_normalizes_tool_arguments_from_object_to_json_string()
    {
        // Ollama returns arguments as a JSON object where OpenAI-shaped APIs return a
        // string; callers must not have to care which provider answered.
        var handler = StubHttpMessageHandler.RespondingWith("""
            {
              "model": "llama3.1",
              "message": {
                "role": "assistant",
                "content": "",
                "tool_calls": [
                  { "function": { "name": "crear_oportunidad", "arguments": { "monto": 150000 } } }
                ]
              },
              "done": true
            }
            """);

        var result = await Build(handler).CompleteAsync(Request, CancellationToken.None);

        var toolCall = Assert.Single(result.ToolCalls);
        Assert.Equal("crear_oportunidad", toolCall.Name);
        Assert.Contains("\"monto\"", toolCall.ArgumentsJson, StringComparison.Ordinal);
        Assert.Equal("tool_calls", result.Usage.FinishReason);
    }

    [Fact]
    public async Task CompleteAsync_sends_tools_and_sampling_options_in_ollamas_own_shape()
    {
        var handler = StubHttpMessageHandler.RespondingWith("""
            { "model": "llama3.1", "message": { "content": "ok" }, "done": true }
            """);

        var request = Request with
        {
            Tools = [new LlmTool("escalar_a_humano", "Pasa la conversación a una persona", """{"type":"object","properties":{}}""")],
        };

        await Build(handler).CompleteAsync(request, CancellationToken.None);

        var body = Assert.Single(handler.ReceivedBodies);
        Assert.Contains("\"num_predict\":800", body, StringComparison.Ordinal);
        Assert.Contains("\"temperature\":0.3", body, StringComparison.Ordinal);
        Assert.Contains("escalar_a_humano", body, StringComparison.Ordinal);
        Assert.Contains("/api/chat", handler.ReceivedUris[0].AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_omits_the_tools_field_when_no_tool_is_enabled()
    {
        // Some servers reject an empty tools array, so it must be absent rather than [].
        var handler = StubHttpMessageHandler.RespondingWith("""
            { "model": "llama3.1", "message": { "content": "ok" }, "done": true }
            """);

        await Build(handler).CompleteAsync(Request, CancellationToken.None);

        Assert.DoesNotContain("\"tools\"", Assert.Single(handler.ReceivedBodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_yields_deltas_then_a_final_chunk_carrying_usage()
    {
        var handler = StubHttpMessageHandler.RespondingWithNdjson(
            """{"model":"llama3.1","message":{"content":"Hola"},"done":false}""",
            """{"model":"llama3.1","message":{"content":" mundo"},"done":false}""",
            """{"model":"llama3.1","message":{"content":""},"done":true,"done_reason":"stop","prompt_eval_count":5,"eval_count":2}""");

        var chunks = new List<LlmChunk>();
        await foreach (var chunk in Build(handler).StreamAsync(Request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Equal("Hola mundo", string.Concat(chunks.Select(chunk => chunk.DeltaContent)));
        Assert.True(chunks[^1].IsFinal);
        Assert.Equal(5, chunks[^1].Usage!.TokensIn);
        Assert.Equal(2, chunks[^1].Usage!.TokensOut);
    }

    [Fact]
    public async Task A_server_error_is_transient_so_the_resilience_layer_retries_it()
    {
        var handler = StubHttpMessageHandler.RespondingWith("overloaded", HttpStatusCode.ServiceUnavailable);

        var failure = await Assert.ThrowsAsync<LlmProviderException>(
            () => Build(handler).CompleteAsync(Request, CancellationToken.None));

        Assert.True(failure.IsTransient);
        Assert.Equal("ollama", failure.ProviderName);
    }

    [Fact]
    public async Task An_unknown_model_is_permanent_so_it_is_not_retried()
    {
        var handler = StubHttpMessageHandler.RespondingWith("""{"error":"model not found"}""", HttpStatusCode.NotFound);

        var failure = await Assert.ThrowsAsync<LlmProviderException>(
            () => Build(handler).CompleteAsync(Request, CancellationToken.None));

        Assert.False(failure.IsTransient);
    }

    [Fact]
    public async Task A_provider_that_is_not_running_reports_a_transient_failure()
    {
        // Nothing listening on the port: the "un proveedor se cae" case ORB-C01 wants
        // failover to cover.
        var provider = new OllamaLlmProvider(
            new HttpClient(new UnreachableHttpMessageHandler()) { BaseAddress = new Uri("http://localhost:11434/") },
            new StubModelSelector("llama3.1", "nomic-embed-text"));

        var failure = await Assert.ThrowsAsync<LlmProviderException>(
            () => provider.CompleteAsync(Request, CancellationToken.None));

        Assert.True(failure.IsTransient);
    }

    [Fact]
    public async Task EmbedAsync_returns_the_vector_for_the_single_input_sent()
    {
        var handler = StubHttpMessageHandler.RespondingWith("""
            { "model": "nomic-embed-text", "embeddings": [[0.1, 0.2, 0.3]], "prompt_eval_count": 4 }
            """);

        var result = await Build(handler).EmbedAsync("catálogo de panadería", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal([0.1f, 0.2f, 0.3f], result.Vector);
        Assert.Equal(4, result.Usage.TokensIn);
        Assert.Equal(0, result.Usage.TokensOut);
    }

    [Fact]
    public void IsConfigured_is_false_without_a_base_address()
        => Assert.False(new OllamaLlmProvider(new HttpClient(), new StubModelSelector("llama3.1", "nomic-embed-text")).IsConfigured);

    private sealed class UnreachableHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Connection refused");
    }
}
