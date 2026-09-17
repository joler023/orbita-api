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
    /// <para>The page and its total come back <b>from one query</b>, and that is not a
    /// micro-optimization: two statements in one transaction each get their own snapshot
    /// under READ COMMITTED, so a handoff committing between them produced a page of zero
    /// rows reporting a total of one — "mostrando 0 de 1" on a screen whose whole job is
    /// to be trusted about who is waiting. A parallel test run caught it.</para>
    /// <param name="waitingSince">Exclusive lower bound from the cursor; null starts at the front.</param>
    Task<HandoffQueuePageResult> ListWaitingForHumanAsync(
        Guid tenantId,
        DateTimeOffset? waitingSince,
        int limit,
        CancellationToken cancellationToken);
}

/// <param name="Total">
/// How many are waiting in the whole queue, not just on this page, so a screen can say
/// "50 de 137" instead of quietly showing the first page as if it were all of them.
/// </param>
public sealed record HandoffQueuePageResult(IReadOnlyList<HandoffQueueEntry> Entries, int Total);

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
    string ContactName,
    HandoffReason Reason,
    DateTimeOffset RequestedAt,
    string? Summary,
    DateTimeOffset? LastMessageAt,
    string? LastMessagePreview);
