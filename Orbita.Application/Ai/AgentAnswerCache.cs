using Orbita.Domain.Ai;
using Orbita.Domain.Common;

namespace Orbita.Application.Ai;

/// <summary>
/// ORB-C12: the lookup and the write the responder does around a model call. Kept apart
/// from the responder so the rules about <em>what</em> is safe to reuse stay in one place.
/// </summary>
public interface IAgentAnswerCache
{
    /// <summary>
    /// Embeds the question and looks for an earlier answer. The embedding run is staged
    /// (not saved) with <see cref="AgentAnswerCache.HitReason"/> or
    /// <see cref="AgentAnswerCache.MissReason"/> as its finish reason, which is what makes
    /// the hit rate readable straight off <c>ai_runs</c>.
    /// </summary>
    Task<AgentCacheLookup> LookupAsync(
        Guid tenantId, AiAgent agent, Guid conversationId, string question, CancellationToken cancellationToken);

    /// <summary>Stages the answer under the lookup's fingerprint. Saved by the caller's own unit of work.</summary>
    void Store(Guid tenantId, AiAgent agent, AgentCacheLookup lookup, string question, string answer);
}

/// <param name="RunId">The embedding run — on a hit, the reply's <c>ai_run_id</c>.</param>
public sealed record AgentCacheLookup(
    AgentAnswerCacheHit? Hit, IReadOnlyList<float> Embedding, string Fingerprint, Guid RunId);

public sealed class AgentAnswerCache(
    IAgentAnswerCacheRepository entries,
    IKnowledgeDocumentRepository documents,
    ILlmProvider llmProvider,
    IAiRunRecorder runRecorder,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAgentAnswerCache
{
    public const string HitReason = "cache_hit";

    public const string MissReason = "cache_miss";

    public async Task<AgentCacheLookup> LookupAsync(
        Guid tenantId, AiAgent agent, Guid conversationId, string question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var threshold = agent.SemanticCacheThreshold
            ?? throw new InvalidOperationException("The semantic cache is off for this assistant.");

        var embedding = await llmProvider.EmbedAsync(question.Trim(), tenantId, cancellationToken);

        var (hit, fingerprint) = await unitOfWork.QueryInTenantScopeAsync(
            async ct =>
            {
                var (count, latest) = await documents.SummarizeForAgentAsync(tenantId, agent.Id, ct);
                var current = AgentAnswerFingerprint.Compute(agent, count, latest);
                var found = await entries.FindSimilarAsync(
                    tenantId, agent.Id, embedding.Vector, current, (double)threshold, ct);

                return (found, current);
            },
            cancellationToken);

        var runId = runRecorder.Record(
            tenantId,
            agent.Id,
            embedding.Usage with { FinishReason = hit is null ? MissReason : HitReason },
            conversationId);

        return new AgentCacheLookup(hit, embedding.Vector, fingerprint, runId);
    }

    public void Store(Guid tenantId, AiAgent agent, AgentCacheLookup lookup, string question, string answer)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(lookup);

        entries.Add(AgentAnswerCacheEntry.Create(
            tenantId, agent.Id, question, lookup.Embedding, answer, lookup.Fingerprint, timeProvider.GetUtcNow()));
    }
}
