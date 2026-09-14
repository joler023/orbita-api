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
}
