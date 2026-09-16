namespace Orbita.Domain.Inbox;

public interface IMessageRepository
{
    /// <summary>Idempotency check: a message already recorded under this external id is never re-inserted.</summary>
    Task<Message?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken);

    Task<Message?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The tail of a conversation, oldest first, for ORB-C04's prompt context.
    ///
    /// Bounded by <paramref name="limit"/> rather than paged: everything returned goes
    /// into a prompt that is paid for by the token, so "the last N turns" is the only
    /// shape this needs. Ordering is resolved newest-first in the query and reversed for
    /// the caller — taking the *last* N of a long thread is the point.
    /// </summary>
    Task<IReadOnlyList<Message>> GetRecentByConversationAsync(
        Guid tenantId,
        Guid conversationId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// How many times the assistant has answered this conversation since
    /// <paramref name="since"/> — ORB-C06's per-window iteration limit.
    ///
    /// Counts by author rather than by direction: a person's replies are not the ones
    /// the limit is about, and an assistant reply is exactly the row that carries an
    /// <c>ai_run_id</c> and no <c>sent_by_user_id</c>.
    /// </summary>
    Task<int> CountAgentRepliesSinceAsync(
        Guid tenantId,
        Guid conversationId,
        DateTimeOffset since,
        CancellationToken cancellationToken);

    Task AddAsync(Message message, CancellationToken cancellationToken);
}
