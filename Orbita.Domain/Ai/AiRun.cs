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

    /// <summary>
    /// The tool names the model asked for in this call (ORB-C09). One run per model call,
    /// so a two-round tool exchange leaves the first run with the tools and the second
    /// with none — which is exactly what happened, and what billing by action needs.
    /// </summary>
    public IReadOnlyList<string> ToolsCalled => _toolsCalled;

    /// <summary>
    /// Which knowledge fragments were in the prompt (orbita-schema.dbml's
    /// <c>retrieved_chunk_ids</c>). It is what lets someone answer "where did this reply
    /// come from" after the fact, when the documents may already have changed.
    /// </summary>
    public IReadOnlyList<Guid> RetrievedChunkIds => _retrievedChunkIds;

    /// <summary>
    /// Whether this call was the one that handed the conversation to a person
    /// (orbita-schema.dbml's <c>was_handoff</c>). ORB-C09 left the column out because
    /// nothing could ever set it before ORB-C07 existed; the handoff summary is the call
    /// that sets it now.
    ///
    /// It is on the run rather than derived from the conversation because a conversation
    /// can be handed over, given back and handed over again, and "how many handoffs did
    /// this assistant cause, and what did they cost" is a question about calls, not about
    /// the state a thread happens to be in today.
    /// </summary>
    public bool WasHandoff { get; private init; }

    private readonly List<string> _toolsCalled = [];

    private readonly List<Guid> _retrievedChunkIds = [];

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
        DateTimeOffset now,
        IReadOnlyList<string>? toolsCalled = null,
        IReadOnlyList<Guid>? retrievedChunkIds = null,
        bool wasHandoff = false)
    {
        var run = new AiRun(Guid.NewGuid(), tenantId, agentId, conversationId, model, tokensIn, tokensOut, costUsd, latencyMs, finishReason, error: null, now)
        {
            WasHandoff = wasHandoff,
        };

        run._toolsCalled.AddRange(toolsCalled ?? []);
        run._retrievedChunkIds.AddRange(retrievedChunkIds ?? []);

        return run;
    }

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
