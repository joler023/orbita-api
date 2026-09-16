using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;
using Pgvector;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class AgentAnswerCacheRepository(OrbitaDbContext dbContext) : IAgentAnswerCacheRepository
{
    public void Add(AgentAnswerCacheEntry entry) => dbContext.AgentAnswerCache.Add(entry);

    /// <summary>
    /// Cosine similarity through <c>&lt;=&gt;</c>, like ORB-C03's search. An entry under a
    /// stale fingerprint is filtered out in SQL, so it can never win on similarity alone.
    /// </summary>
    public async Task<AgentAnswerCacheHit?> FindSimilarAsync(
        Guid tenantId, Guid agentId, IReadOnlyList<float> embedding, string fingerprint, double threshold, CancellationToken cancellationToken)
    {
        var query = new Vector(embedding.ToArray());

        var rows = await dbContext.Database
            .SqlQuery<CacheRow>($"""
                SELECT  e.id     AS "EntryId",
                        e.answer AS "Answer",
                        1 - (e.embedding <=> {query}) AS "Score"
                FROM    agent_answer_cache e
                WHERE   e.tenant_id   = {tenantId}
                  AND   e.agent_id    = {agentId}
                  AND   e.fingerprint = {fingerprint}
                ORDER BY e.embedding <=> {query}
                LIMIT   1
                """)
            .ToListAsync(cancellationToken);

        return rows is [var best] && best.Score >= threshold
            ? new AgentAnswerCacheHit(best.EntryId, best.Answer, best.Score)
            : null;
    }

    private sealed record CacheRow(Guid EntryId, string Answer, double Score);
}
