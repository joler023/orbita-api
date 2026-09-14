namespace Orbita.Domain.Ai;

/// <summary>
/// What one model call consumed. ORB-C01 requires that "toda llamada registra tokens,
/// costo y latencia", and orbita-schema.dbml's note on <c>ai_runs</c> is emphatic that
/// this measurement cannot be reconstructed after the fact — so every
/// <c>ILlmProvider</c> call returns one of these, whether or not anything persists it
/// yet.
///
/// Property names deliberately mirror the <c>ai_runs</c> columns
/// (<c>model</c>, <c>tokens_in</c>, <c>tokens_out</c>, <c>cost_usd</c>,
/// <c>latency_ms</c>, <c>finish_reason</c>) so ORB-C02 can persist a run without a
/// translation step in between.
/// </summary>
/// <param name="Model">The model id actually used — the resolved one, not the requested task.</param>
/// <param name="TokensIn">Prompt tokens. Zero when a provider does not report them.</param>
/// <param name="TokensOut">Completion tokens. Zero for embeddings, which produce no text.</param>
/// <param name="CostUsd">
/// Billed cost in USD. Exactly <c>0</c> for a locally hosted provider (Ollama), which
/// is a real measurement and not a missing one — cost is a function of where the model
/// ran, so the provider that ran it is what knows.
/// </param>
/// <param name="LatencyMs">Wall-clock duration of the call, measured by the adapter around its own HTTP request.</param>
/// <param name="FinishReason">
/// Why generation stopped (<c>stop</c> | <c>length</c> | <c>tool_calls</c> |
/// <c>content_filter</c>), normalized across providers. Null for embeddings.
/// </param>
public sealed record LlmUsage(
    string Model,
    int TokensIn,
    int TokensOut,
    decimal CostUsd,
    int LatencyMs,
    string? FinishReason);
