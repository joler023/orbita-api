using Orbita.Application.Common;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Inbox;

/// <summary>
/// ORB-C07: taking a conversation away from the assistant and leaving it for a person.
///
/// Split from <see cref="IOutboundMessageService"/> on purpose. Sending is about a
/// message; this is about who owns the thread, and the two have different callers: the
/// assistant's own runtime asks for a handoff, while a person in the dashboard asks for
/// the queue and gives conversations back.
/// </summary>
public interface IConversationHandoffService
{
    /// <summary>
    /// Hands the conversation over and records the handoff, in one transaction and without
    /// calling a model — the customer who asked for a person is waiting on this. The note
    /// for the person taking over is written afterwards by <see cref="WriteSummaryAsync"/>,
    /// reacting to the <c>conversation.handoff_requested</c> event this stages.
    ///
    /// No caller id and no permission check: this is called by the assistant's runtime,
    /// not by a person — the same reasoning as <c>SendAgentReplyAsync</c>. Never throws
    /// for "already handed over" or "no such conversation": both are ordinary outcomes on
    /// a path that is reacting to a customer's message, and an exception would turn a
    /// customer reaching a person into a failed background job.
    /// </summary>
    /// <param name="summary">
    /// The note for whoever takes over, when the caller already has one — the assistant
    /// handing over through <c>escalar_a_humano</c> writes it itself, and it is the one
    /// party in the exchange that already holds the whole context. Null asks for one to be
    /// generated, which costs a cheap model call.
    /// </param>
    /// <returns>True when this call is the one that handed it over.</returns>
    Task<bool> RequestAsync(
        Guid tenantId,
        Guid conversationId,
        Guid agentId,
        HandoffReason reason,
        string? summary,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the note for whoever takes over a handed-over conversation, with one cheap
    /// model call, and records that call as the handoff's (<c>ai_runs.was_handoff</c>).
    ///
    /// A no-op when there is nothing to write: the assistant left its own note, a person
    /// already gave the conversation back, or it is gone. A provider failure is
    /// <b>not</b> swallowed — the caller is the outbox, which retries with backoff, so an
    /// outage delays the note instead of losing it.
    /// </summary>
    Task WriteSummaryAsync(
        Guid tenantId,
        Guid conversationId,
        Guid agentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The queue, oldest wait first, with the totals a screen needs to avoid implying the
    /// first page is all of it. Requires <see cref="Domain.Identity.Permission.ViewInbox"/>.
    /// </summary>
    Task<HandoffQueuePage> ListWaitingAsync(
        Guid tenantId,
        Guid callerUserId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives the conversation back to the assistant — ORB-C07's "salvo que un humano lo
    /// reactive". Requires <see cref="Domain.Identity.Permission.SendMessages"/>: handing
    /// a customer back to a machine is acting on the conversation, not viewing it.
    ///
    /// Idempotent: a conversation the assistant already owns is the state the caller
    /// asked for, so this answers with that state instead of an error.
    /// </summary>
    /// <exception cref="ConversationNotFoundException">No such conversation in this tenant.</exception>
    Task<HandoffStateDto> ReturnToAssistantAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid conversationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// <c>{ items, nextCursor }</c>, the shape agreed with the frontend for every list in the
/// product (see <see cref="CursorPage{T}"/>), plus a total.
///
/// It is its own record rather than <c>CursorPage&lt;HandoffDto&gt;</c> because of that
/// total, and the total is here because a queue is the one list whose size is itself the
/// information: "50 de 137" is the difference between a team that is coping and one that
/// is not, and a page without it lets a screen show the first page as though it were all
/// of them. The frontend asked for this after pointing out that a queue is precisely what
/// grows on the worst day the business has.
/// </summary>
public sealed record HandoffQueuePage(IReadOnlyList<HandoffDto> Items, string? NextCursor, int Total);

/// <param name="Reason">
/// Stable enum name, mapped to copy by the dashboard. Adding a value is a contract change
/// to announce: the frontend's rule is to stay silent on a reason it does not know rather
/// than invent one.
/// </param>
/// <param name="ContactName">
/// Never null: <c>contacts.display_name</c> is NOT NULL and the queue joins it inner. The
/// frontend asked, having seen <c>string?</c> here while the contract said it always
/// comes; the type was the one that was wrong.
/// </param>
public sealed record HandoffDto(
    Guid ConversationId,
    Guid ContactId,
    string ContactName,
    HandoffReason Reason,
    DateTimeOffset RequestedAt,
    string? Summary,
    DateTimeOffset? LastMessageAt,
    string? LastMessagePreview)
{
    public static HandoffDto From(HandoffQueueEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new HandoffDto(
            entry.ConversationId,
            entry.ContactId,
            entry.ContactName,
            entry.Reason,
            entry.RequestedAt,
            entry.Summary,
            entry.LastMessageAt,
            entry.LastMessagePreview);
    }
}

/// <summary>Whether a conversation is waiting for a person right now, and since when.</summary>
public sealed record HandoffStateDto(
    Guid ConversationId,
    bool IsWaitingForHuman,
    HandoffReason? Reason,
    DateTimeOffset? RequestedAt,
    string? Summary)
{
    public static HandoffStateDto From(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        return new HandoffStateDto(
            conversation.Id,
            conversation.IsWaitingForHuman,
            conversation.HandoffReason,
            conversation.HandoffRequestedAt,
            conversation.HandoffSummary);
    }
}
