using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Inbox;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class MessageRepository(OrbitaDbContext dbContext) : IMessageRepository
{
    public Task<Message?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken)
        => dbContext.Messages.SingleOrDefaultAsync(m => m.ExternalId == externalId, cancellationToken);

    public Task<Message?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Messages.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    /// <summary>
    /// Newest-first in the query so the index and the LIMIT do the work, then reversed in
    /// memory so the caller gets the turns in the order they were said. Reversing N rows
    /// where N is a prompt-sized bound is free; making Postgres sort the whole thread
    /// ascending to take the tail would not be.
    /// </summary>
    public async Task<IReadOnlyList<Message>> GetRecentByConversationAsync(
        Guid tenantId,
        Guid conversationId,
        int limit,
        CancellationToken cancellationToken)
    {
        var newestFirst = await dbContext.Messages
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        newestFirst.Reverse();

        return newestFirst;
    }

    public Task<int> CountAgentRepliesSinceAsync(
        Guid tenantId,
        Guid conversationId,
        DateTimeOffset since,
        CancellationToken cancellationToken)
        => dbContext.Messages
            .Where(m => m.TenantId == tenantId
                && m.ConversationId == conversationId
                && m.CreatedAt >= since
                && m.AiRunId != null)
            .CountAsync(cancellationToken);

    public async Task AddAsync(Message message, CancellationToken cancellationToken)
        => await dbContext.Messages.AddAsync(message, cancellationToken);
}
