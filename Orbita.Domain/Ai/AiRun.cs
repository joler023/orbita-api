using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// One recorded call to a model — orbita-schema.dbml's <c>ai_runs</c>, whose note is
/// blunt about why it exists from day one: "Medición de consumo desde el día 1. Es lo
/// que hace posible cobrar por uso; no se puede reconstruir a posteriori."
///
/// ORB-A13 (usage metering) and ORB-A12 (billing) are both computed from this table, and
/// ORB-C09 later enriches it with the per-turn detail of a real conversation. C02 writes
/// the first rows: the embedding calls made while indexing a document.
///
/// Immutable after construction, like <c>AuditLogEntry</c> — a run is a fact about
/// something that already happened.
/// </summary>
public sealed class AiRun : Entity
{
    private AiRun(
        Guid id,
        Guid tenantId,
        Guid agentId,
        Guid? conversationId,
        string model,
        int tokensIn,
        int tokensOut,
        decimal costUsd,
        int? latencyMs,
        string? finishReason,
        string? error,
        DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        AgentId = agentId;
        ConversationId = conversationId;
        Model = model;
        TokensIn = tokensIn;
        TokensOut = tokensOut;
        CostUsd = costUsd;
        LatencyMs = latencyMs;
        FinishReason = finishReason;
        Error = error;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; }

    public Guid AgentId { get; }

    /// <summary>Null for work not tied to a conversation — indexing a document, or ORB-C11's test bench.</summary>
    public Guid? ConversationId { get; }

    public string Model { get; }

    public int TokensIn { get; }

    public int TokensOut { get; }

    /// <summary>
    /// Zero for a locally hosted model, which is a real measurement. It is also what a
    /// paid model records when nobody configured its price — see
    /// <c>ConfigurationLlmPricing</c>, which warns about exactly that case.
    /// </summary>
    public decimal CostUsd { get; }

    public int? LatencyMs { get; }

    public string? FinishReason { get; }

    /// <summary>Set when the call failed. A failed run is still recorded — it consumed time, and often tokens.</summary>
    public string? Error { get; }

    public DateTimeOffset CreatedAt { get; }

    public static AiRun Record(
        Guid tenantId,
        Guid agentId,
        Guid? conversationId,
        string model,
        int tokensIn,
        int tokensOut,
        decimal costUsd,
        int? latencyMs,
        string? finishReason,
        DateTimeOffset now)
        => new(Guid.NewGuid(), tenantId, agentId, conversationId, model, tokensIn, tokensOut, costUsd, latencyMs, finishReason, error: null, now);

    public static AiRun RecordFailure(
        Guid tenantId,
        Guid agentId,
        Guid? conversationId,
        string model,
        string error,
        int? latencyMs,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return new AiRun(Guid.NewGuid(), tenantId, agentId, conversationId, model, 0, 0, 0m, latencyMs, finishReason: null, error, now);
    }
}
