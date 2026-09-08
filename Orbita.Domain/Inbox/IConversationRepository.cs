namespace Orbita.Domain.Inbox;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The single lifetime conversation thread between this contact and this specific
    /// channel account, regardless of status — a contact keeps one thread per account
    /// that cycles Open/Pending/Snoozed/Closed rather than spawning a new row every time
    /// the previous one was closed. <see cref="Conversation.RegisterInbound"/> is what
    /// actually reopens it; this just finds it (or null, if this is truly the first message).
    /// </summary>
    Task<Conversation?> FindOpenByContactAndAccountAsync(Guid contactId, Guid channelAccountId, CancellationToken cancellationToken);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);
}
