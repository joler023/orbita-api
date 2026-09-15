namespace Orbita.Domain.Inbox;

public interface IMessageRepository
{
    /// <summary>Idempotency check: a message already recorded under this external id is never re-inserted.</summary>
    Task<Message?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken);

    Task<Message?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(Message message, CancellationToken cancellationToken);
}
