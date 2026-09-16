using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class KnowledgeDocumentRepository(OrbitaDbContext dbContext) : IKnowledgeDocumentRepository
{
    public void Add(KnowledgeDocument document) => dbContext.KnowledgeDocuments.Add(document);

    public void Remove(KnowledgeDocument document) => dbContext.KnowledgeDocuments.Remove(document);

    public Task<KnowledgeDocument?> GetByIdAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
        => dbContext.KnowledgeDocuments
            .SingleOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<KnowledgeDocument>> ListByAgentAsync(
        Guid tenantId,
        Guid agentId,
        DateTimeOffset? createdBefore,
        int limit,
        CancellationToken cancellationToken)
        => await dbContext.KnowledgeDocuments
            .Where(d => d.TenantId == tenantId && d.AgentId == agentId)
            // Keyset, not offset: the cursor is the previous page's last CreatedAt, so
            // paging stays O(limit) however deep the list goes.
            .Where(d => createdBefore == null || d.CreatedAt < createdBefore)
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<(int Count, DateTimeOffset? LatestChange)> SummarizeForAgentAsync(
        Guid tenantId, Guid agentId, CancellationToken cancellationToken)
    {
        // Deleting a document lowers the count; uploading one raises it and moves
        // created_at; reindexing moves indexed_at - every path that changes what retrieval
        // would return changes this pair, and so changes the fingerprint built from it.
        var query = dbContext.KnowledgeDocuments.Where(d => d.TenantId == tenantId && d.AgentId == agentId);

        var count = await query.CountAsync(cancellationToken);

        if (count == 0)
        {
            return (0, null);
        }

        var latestCreated = await query.MaxAsync(d => (DateTimeOffset?)d.CreatedAt, cancellationToken);
        var latestIndexed = await query.MaxAsync(d => d.IndexedAt, cancellationToken);

        return (count, latestIndexed > latestCreated ? latestIndexed : latestCreated);
    }
}
