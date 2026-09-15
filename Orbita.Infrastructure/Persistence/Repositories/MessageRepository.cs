using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Inbox;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class MessageRepository(OrbitaDbContext dbContext) : IMessageRepository
{
    public Task<Message?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken)
        => dbContext.Messages.SingleOrDefaultAsync(m => m.ExternalId == externalId, cancellationToken);

    public Task<Message?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Messages.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task AddAsync(Message message, CancellationToken cancellationToken)
        => await dbContext.Messages.AddAsync(message, cancellationToken);
}
