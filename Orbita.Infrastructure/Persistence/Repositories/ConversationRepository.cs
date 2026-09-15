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
}
