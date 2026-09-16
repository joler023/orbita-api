using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Inbox;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class ConversationRepository(OrbitaDbContext dbContext) : IConversationRepository
{
    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Conversations.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Conversation?> FindOpenByContactAndAccountAsync(Guid contactId, Guid channelAccountId, CancellationToken cancellationToken)
        => dbContext.Conversations.SingleOrDefaultAsync(
            c => c.ContactId == contactId && c.ChannelAccountId == channelAccountId,
            cancellationToken);

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
        => await dbContext.Conversations.AddAsync(conversation, cancellationToken);

    /// <summary>
    /// Joined to <c>contacts</c> in the query rather than resolved afterwards: the caller
    /// needs a name per row, and doing it here is one round trip instead of one per
    /// conversation. Both tables carry the same tenant query filter, so the join cannot
    /// reach across tenants even before Row Level Security has its say.
    /// </summary>
    public async Task<IReadOnlyList<HandoffQueueEntry>> ListWaitingForHumanAsync(
        Guid tenantId,
        DateTimeOffset? waitingSince,
        int limit,
        CancellationToken cancellationToken)
        => await dbContext.Conversations
            .Where(c => c.TenantId == tenantId && c.HandoffRequestedAt != null)
            .Where(c => waitingSince == null || c.HandoffRequestedAt > waitingSince)
            .OrderBy(c => c.HandoffRequestedAt)
            .Join(
                dbContext.Contacts,
                conversation => conversation.ContactId,
                contact => contact.Id,
                (conversation, contact) => new HandoffQueueEntry(
                    conversation.Id,
                    conversation.ContactId,
                    contact.DisplayName,
                    conversation.HandoffReason!.Value,
                    conversation.HandoffRequestedAt!.Value,
                    conversation.HandoffSummary,
                    conversation.LastMessageAt,
                    conversation.LastMessagePreview))
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<int> CountWaitingForHumanAsync(Guid tenantId, CancellationToken cancellationToken)
        => dbContext.Conversations
            .Where(c => c.TenantId == tenantId && c.HandoffRequestedAt != null)
            .CountAsync(cancellationToken);
}
