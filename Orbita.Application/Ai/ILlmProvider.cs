namespace Orbita.Application.Ai;

/// <summary>
/// One model provider behind ORB-C01 — "no dependemos de un solo proveedor de IA
/// porque el mercado cambia de precio y de capacidades cada pocos meses".
///
/// Two implementations ship in Infrastructure: <c>OllamaLlmProvider</c> (a locally
/// hosted model, free, no account) and <c>OpenAiCompatibleLlmProvider</c> (the de-facto
/// <c>/v1/chat/completions</c> shape, pointed at LM Studio or llama.cpp locally today
/// and re-pointable at a hosted provider by changing only configuration). Application
/// code never resolves these directly: it resolves this interface and gets
/// <see cref="ResilientLlmProvider"/>, which adds retry and failover across them.
///
/// Every method returns an <c>LlmUsage</c> because ORB-C01 requires that every call
/// records tokens, cost and latency, and orbita-schema.dbml's <c>ai_runs</c> note warns
/// that measurement skipped now cannot be reconstructed later.
///
/// Adapters signal failure with <see cref="LlmProviderException"/> and nothing else, so
/// the resilience layer can tell a provider being down from a request being wrong.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Stable identifier used in logs, failover messages and <c>ai_runs</c> attribution.</summary>
    string Name { get; }

    /// <summary>
    /// False when this provider has no usable configuration (no base URL, or a hosted
    /// endpoint with no API key) — the same "code complete, credentials empty" posture
    /// ORB-A12 uses for Stripe and Wompi. Unconfigured providers are skipped rather
    /// than tried and failed.
    /// </summary>
    bool IsConfigured { get; }

    /// <exception cref="LlmProviderException">The call failed; see <c>IsTransient</c>.</exception>
    Task<LlmCompletionResult> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The same call as <see cref="CompleteAsync"/>, surfaced incrementally. The last
    /// chunk carries <c>IsFinal = true</c> and the usage totals.
    /// </summary>
    /// <exception cref="LlmProviderException">The call failed; see <c>IsTransient</c>.</exception>
    IAsyncEnumerable<LlmChunk> StreamAsync(LlmCompletionRequest request, CancellationToken cancellationToken);

    /// <exception cref="LlmProviderException">The call failed; see <c>IsTransient</c>.</exception>
    Task<LlmEmbeddingResult> EmbedAsync(string text, string model, CancellationToken cancellationToken);
}
