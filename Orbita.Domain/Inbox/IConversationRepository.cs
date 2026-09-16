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

    /// <summary>
    /// The queue of conversations waiting for a person (ORB-C07), oldest wait first.
    ///
    /// Oldest first, not newest: this is a queue of people waiting, and the one who has
    /// waited longest is the one closest to giving up. Newest-first would starve exactly
    /// them, and it is the order that looks fine in a demo with three rows and is wrong
    /// every day after that.
    ///
    /// Keyset paged on <see cref="Conversation.HandoffRequestedAt"/> rather than capped,
    /// because a queue is precisely the list that grows when the team cannot keep up: a
    /// silent cap would hide the 51st customer on the worst day the business has.
    /// </summary>
    /// <param name="waitingSince">Exclusive lower bound from the cursor; null starts at the front.</param>
    Task<IReadOnlyList<HandoffQueueEntry>> ListWaitingForHumanAsync(
        Guid tenantId,
        DateTimeOffset? waitingSince,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// How many conversations are waiting in total, so a screen can say "50 de 137"
    /// instead of quietly showing the first page as if it were all of them.
    /// </summary>
    Task<int> CountWaitingForHumanAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// One row of the handoff queue, joined with the contact's display name.
///
/// The name is joined here rather than resolved by the caller because the alternative is
/// a screen full of UUIDs: the frontend confirmed it has no contacts API to resolve them
/// with, so the join is paid once here instead of never.
/// </summary>
public sealed record HandoffQueueEntry(
    Guid ConversationId,
    Guid ContactId,
    string? ContactName,
    HandoffReason Reason,
    DateTimeOffset RequestedAt,
    string? Summary,
    DateTimeOffset? LastMessageAt,
    string? LastMessagePreview);
