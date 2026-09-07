using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class KnowledgeChunkRepository(OrbitaDbContext dbContext) : IKnowledgeChunkRepository
{
    public void AddRange(IEnumerable<KnowledgeChunk> chunks) => dbContext.KnowledgeChunks.AddRange(chunks);

    /// <summary>
    /// A bulk delete rather than load-then-remove: a 50-page document can be hundreds of
    /// chunks, and none of them needs to pass through the change tracker to be dropped.
    /// </summary>
    public Task DeleteByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
        => dbContext.KnowledgeChunks
            .Where(c => c.TenantId == tenantId && c.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<int> CountByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
        => dbContext.KnowledgeChunks
            .CountAsync(c => c.TenantId == tenantId && c.DocumentId == documentId, cancellationToken);
}
