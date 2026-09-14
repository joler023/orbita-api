using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class KnowledgeIndexingQueue(OrbitaDbContext dbContext) : IKnowledgeIndexingQueue
{
    public void Enqueue(KnowledgeIndexingQueueEntry entry)
    {
        // Re-queueing a document already in the queue is a no-op rather than a duplicate
        // key error — reindexing something that never got picked up is legitimate.
        if (dbContext.KnowledgeIndexingQueue.Local.All(existing => existing.Id != entry.Id))
        {
            dbContext.KnowledgeIndexingQueue.Add(entry);
        }
    }

    public async Task<IReadOnlyList<KnowledgeIndexingQueueEntry>> PeekAsync(int limit, CancellationToken cancellationToken)
        => await dbContext.KnowledgeIndexingQueue
            .AsNoTracking()
            .OrderBy(entry => entry.EnqueuedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task RemoveAsync(Guid documentId, CancellationToken cancellationToken)
        => dbContext.KnowledgeIndexingQueue
            .Where(entry => entry.Id == documentId)
            .ExecuteDeleteAsync(cancellationToken);
}
