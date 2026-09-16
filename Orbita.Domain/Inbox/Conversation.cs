using Orbita.Domain.Common;

namespace Orbita.Domain.Inbox;

/// <summary>
/// One thread of messages between a tenant and a contact over a specific channel
/// account (ORB-B03). <see cref="WindowExpiresAt"/> models Meta's 24h customer-service
/// window as an explicit business rule, not an infrastructure detail — once it lapses,
/// only an approved template can restart the conversation (ORB-B07) until B11 extends
/// Instagram's window with the human_agent tag.
/// </summary>
public sealed class Conversation : Entity
{
    public const int LastMessagePreviewMaxLength = 140;

    /// <summary>
    /// Long enough for a few sentences of context and short enough that nobody treats it
    /// as a transcript — the messages themselves are right there for whoever wants them.
    /// </summary>
    public const int HandoffSummaryMaxLength = 600;

    /// <summary>Meta's WhatsApp customer-service window.</summary>
    public static readonly TimeSpan ServiceWindow = TimeSpan.FromHours(24);

    private Conversation(
        Guid id,
        Guid tenantId,
        Guid contactId,
        Guid channelAccountId,
        ConversationStatus status,
        DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        ContactId = contactId;
        ChannelAccountId = channelAccountId;
        Status = status;
        CreatedAt = createdAt;
        UnreadCount = 0;
    }

    public Guid TenantId { get; }

    public Guid ContactId { get; }

    public Guid ChannelAccountId { get; }

    public ConversationStatus Status { get; private set; }

    /// <summary>Human agent assigned to this conversation; null = unassigned. Set by ORB-B15 (not yet implemented).</summary>
    public Guid? AssigneeId { get; private set; }

    /// <summary>AI agent in charge; null = humans only. Set once Track C's agents exist.</summary>
    public Guid? AiAgentId { get; private set; }

    public DateTimeOffset? WindowExpiresAt { get; private set; }

    /// <summary>Instagram-only extension to 7 days via the human_agent tag (ORB-B11, not yet implemented).</summary>
    public DateTimeOffset? HumanAgentExpiresAt { get; private set; }

    public int UnreadCount { get; private set; }

    public DateTimeOffset? LastMessageAt { get; private set; }

    /// <summary>Not in orbita-schema.dbml — avoids a LATERAL join against the (partitioned) messages table on the hottest inbox query.</summary>
    public string? LastMessagePreview { get; private set; }

    /// <summary>Seconds between the last inbound message and the first human reply to it. Set once per open window.</summary>
    public int? FirstResponseSeconds { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// When this conversation was handed to a person (ORB-C07); null means the assistant
    /// still has it. Not in orbita-schema.dbml, same class of addition as
    /// <see cref="LastMessagePreview"/>.
    ///
    /// It exists as its own column rather than being inferred from
    /// <see cref="ConversationStatus.Pending"/> because the two answer different questions.
    /// Status says where the thread is in the inbox and changes for ordinary reasons — the
    /// next inbound message reopens a Pending thread, which is right for a thread nobody
    /// got to and wrong for one a person was asked to take over. This is what makes a
    /// handoff stick until a human undoes it.
    /// </summary>
    public DateTimeOffset? HandoffRequestedAt { get; private set; }

    /// <summary>Why it was handed over. Null exactly when <see cref="HandoffRequestedAt"/> is.</summary>
    public HandoffReason? HandoffReason { get; private set; }

    /// <summary>
    /// What the person taking over needs to know, written once at the moment of the
    /// handoff. Null when the summary could not be produced — a model outage must not stop
    /// a customer from reaching a person, so a handoff without its summary is a handoff,
    /// not a failure.
    /// </summary>
    public string? HandoffSummary { get; private set; }

    /// <summary>Whether a person, and not the assistant, owns this conversation right now.</summary>
    public bool IsWaitingForHuman => HandoffRequestedAt is not null;

    public static Conversation Open(Guid tenantId, Guid contactId, Guid channelAccountId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new Conversation(Guid.NewGuid(), tenantId, contactId, channelAccountId, ConversationStatus.Open, now);
    }

    /// <summary>
    /// Reopens if idle (Closed/Pending/Snoozed), refreshes the service window, and bumps
    /// the unread count.
    ///
    /// A conversation waiting for a person is the one case that does not reopen: reopening
    /// it would flip it back to <see cref="ConversationStatus.Open"/> and hand it to the
    /// assistant again on the customer's very next message, which is precisely what
    /// ORB-C07's last criterion forbids ("una vez traspasada, el agente no vuelve a
    /// intervenir salvo que un humano lo reactive"). Everything else still happens: the
    /// window is refreshed and the message counts as unread, because a person does have to
    /// read it.
    /// </summary>
    public void RegisterInbound(DateTimeOffset now, string? preview)
    {
        if (!IsWaitingForHuman && Status is ConversationStatus.Closed or ConversationStatus.Pending or ConversationStatus.Snoozed)
        {
            Status = ConversationStatus.Open;
            ClosedAt = null;
        }

        WindowExpiresAt = now + ServiceWindow;
        UnreadCount++;
        LastMessageAt = now;
        LastMessagePreview = Truncate(preview);
    }

    /// <summary>
    /// <paramref name="isHuman"/> gates <see cref="FirstResponseSeconds"/>: an AI/system
    /// reply doesn't count toward the human first-response SLA. The elapsed time is
    /// measured against the inbound message this is presumably replying to (the
    /// previous <see cref="LastMessageAt"/>, captured before this call overwrites it).
    /// </summary>
    public void RegisterOutbound(DateTimeOffset now, string? preview, bool isHuman)
    {
        if (isHuman && FirstResponseSeconds is null && LastMessageAt is { } lastInbound)
        {
            FirstResponseSeconds = (int)Math.Max(0, (now - lastInbound).TotalSeconds);
        }

        LastMessageAt = now;
        LastMessagePreview = Truncate(preview);
    }

    /// <summary>
    /// Records which assistant is handling this thread (ORB-C04). The column has existed
    /// since ORB-B03 but nothing ever filled it — an assistant that answers without
    /// leaving that trace would make the inbox unable to say who wrote a reply, and
    /// would let two different assistants answer alternate turns of one conversation.
    ///
    /// Idempotent, and deliberately not a reassignment: whichever assistant took the
    /// conversation keeps it. Moving a live conversation to a different assistant is
    /// ORB-C08's decision to make, not a side effect of answering a message.
    /// </summary>
    public void AssignAgent(Guid agentId)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        AiAgentId ??= agentId;
    }

    /// <summary>
    /// Hands the conversation to a person (ORB-C07): it leaves the assistant, lands in the
    /// queue as <see cref="ConversationStatus.Pending"/>, and stays there until a human
    /// gives it back through <see cref="ReturnToAssistant"/>.
    ///
    /// Idempotent, and the <em>first</em> reason wins rather than the last. A customer who
    /// asks for a person and then, still waiting, writes three more angry messages has not
    /// changed why they are in the queue — overwriting it would relabel "me pidieron un
    /// humano" as "parecía molesto" and lose the only fact the person taking over can act
    /// on. It also keeps <see cref="HandoffRequestedAt"/> honest as the moment the wait
    /// started, which is what orders the queue.
    ///
    /// No assignee is set: this marks the conversation as needing a person, not as being
    /// somebody's in particular. Choosing who takes it is ORB-B15.
    /// </summary>
    public void RequestHumanHandoff(HandoffReason reason, string? summary, DateTimeOffset now)
    {
        if (IsWaitingForHuman)
        {
            return;
        }

        HandoffRequestedAt = now;
        HandoffReason = reason;
        HandoffSummary = Truncate(summary, HandoffSummaryMaxLength);
        Status = ConversationStatus.Pending;
    }

    /// <summary>
    /// Adds the note for whoever takes over, after the handoff itself is already committed.
    ///
    /// Separate from <see cref="RequestHumanHandoff"/> because writing it costs a model
    /// call of several seconds, and the customer who asked for a person must not wait for
    /// a note written for somebody else. Only fills an empty one on a conversation still
    /// waiting: a note the assistant wrote itself is not overwritten, and a conversation a
    /// person already gave back does not need one.
    /// </summary>
    /// <returns>Whether the note was attached.</returns>
    public bool AttachHandoffSummary(string summary)
    {
        if (!IsWaitingForHuman || HandoffSummary is not null || string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        HandoffSummary = Truncate(summary.Trim(), HandoffSummaryMaxLength);

        return true;
    }

    /// <summary>
    /// Gives the conversation back to the assistant — the "salvo que un humano lo
    /// reactive" half of ORB-C07's last criterion, and the only way out of the queue.
    ///
    /// Idempotent for the same reason discarding a nonexistent draft is a 200: a
    /// conversation the assistant already owns is the state the caller asked for. The
    /// summary is cleared with the rest, because it described a handoff that is over; the
    /// event that recorded it is what survives.
    /// </summary>
    public void ReturnToAssistant()
    {
        if (!IsWaitingForHuman)
        {
            return;
        }

        HandoffRequestedAt = null;
        HandoffReason = null;
        HandoffSummary = null;
        Status = ConversationStatus.Open;
        ClosedAt = null;
    }

    public bool IsWindowOpen(DateTimeOffset now) => WindowExpiresAt is { } expiresAt && now < expiresAt;

    /// <summary>Whether a free-form (non-template) message can be sent right now. B11 extends this for Instagram's human_agent tag.</summary>
    public bool CanSendFreeForm(DateTimeOffset now) => IsWindowOpen(now);

    public void MarkRead() => UnreadCount = 0;

    public void Close(DateTimeOffset now)
    {
        Status = ConversationStatus.Closed;
        ClosedAt = now;
    }

    private static string? Truncate(string? text, int maxLength = LastMessagePreviewMaxLength)
        => string.IsNullOrEmpty(text) ? text : text[..Math.Min(text.Length, maxLength)];
}
