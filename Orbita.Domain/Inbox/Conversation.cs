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

    public static Conversation Open(Guid tenantId, Guid contactId, Guid channelAccountId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new Conversation(Guid.NewGuid(), tenantId, contactId, channelAccountId, ConversationStatus.Open, now);
    }

    /// <summary>Reopens if idle (Closed/Pending/Snoozed), refreshes the service window, and bumps the unread count.</summary>
    public void RegisterInbound(DateTimeOffset now, string? preview)
    {
        if (Status is ConversationStatus.Closed or ConversationStatus.Pending or ConversationStatus.Snoozed)
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

    public bool IsWindowOpen(DateTimeOffset now) => WindowExpiresAt is { } expiresAt && now < expiresAt;

    /// <summary>Whether a free-form (non-template) message can be sent right now. B11 extends this for Instagram's human_agent tag.</summary>
    public bool CanSendFreeForm(DateTimeOffset now) => IsWindowOpen(now);

    public void MarkRead() => UnreadCount = 0;

    public void Close(DateTimeOffset now)
    {
        Status = ConversationStatus.Closed;
        ClosedAt = now;
    }

    private static string? Truncate(string? preview)
        => string.IsNullOrEmpty(preview) ? preview : preview[..Math.Min(preview.Length, LastMessagePreviewMaxLength)];
}
