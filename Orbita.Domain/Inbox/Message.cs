using System.Text.Json;

namespace Orbita.Domain.Inbox;

/// <summary>
/// One message in a conversation (ORB-B03). Does NOT inherit <see cref="Common.Entity"/>
/// — the third exception after <see cref="Audit.AuditLogEntry"/> and
/// <see cref="Channels.InboundWebhookEvent"/>, and for a different reason than either:
/// <see cref="Id"/> is still a client-generated <see cref="Guid"/>, but the table is
/// partitioned by <see cref="CreatedAt"/> (200M+ rows expected), and Postgres requires a
/// partitioned table's primary key to include the partition key — so the real key is the
/// composite <c>(Id, CreatedAt)</c>, which <see cref="Common.Entity"/>'s single-Id
/// equality assumption doesn't model.
/// </summary>
public sealed class Message
{
    public const int ExternalIdMaxLength = 200;
    public const int MediaMimeMaxLength = 120;
    public const int ErrorCodeMaxLength = 60;

    private Message(
        Guid id,
        Guid tenantId,
        Guid conversationId,
        MessageDirection direction,
        MessageCategory category,
        string? body,
        string? mediaKey,
        string? mediaMime,
        string? externalId,
        string? replyToExternalId,
        Guid? templateId,
        string? templateVariablesJson,
        Guid? sentByUserId,
        Guid? aiRunId,
        MessageStatus status,
        DateTimeOffset? sentAt,
        DateTimeOffset? deliveredAt,
        DateTimeOffset? readAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        ConversationId = conversationId;
        Direction = direction;
        Category = category;
        Body = body;
        MediaKey = mediaKey;
        MediaMime = mediaMime;
        ExternalId = externalId;
        ReplyToExternalId = replyToExternalId;
        TemplateId = templateId;
        TemplateVariablesJson = templateVariablesJson;
        SentByUserId = sentByUserId;
        AiRunId = aiRunId;
        Status = status;
        SentAt = sentAt;
        DeliveredAt = deliveredAt;
        ReadAt = readAt;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid ConversationId { get; }

    public MessageDirection Direction { get; }

    /// <summary>Determines what this message actually costs on Meta's side.</summary>
    public MessageCategory Category { get; private set; }

    public string? Body { get; private set; }

    /// <summary>Key of the object in the media store (R2 in production); never served directly, only via a signed URL (ORB-B06).</summary>
    public string? MediaKey { get; private set; }

    public string? MediaMime { get; private set; }

    /// <summary>Meta's own id for this message. Unique — the basis of inbound/outbound idempotency.</summary>
    public string? ExternalId { get; private set; }

    public string? ReplyToExternalId { get; }

    /// <summary>Set when sent outside the service window with an approved template (ORB-B07).</summary>
    public Guid? TemplateId { get; }

    /// <summary>The exact variables this send used, serialized as a JSON string array — the dispatcher needs the raw values, not just the already-rendered <see cref="Body"/>, to call Meta's template send API.</summary>
    public string? TemplateVariablesJson { get; }

    /// <summary>Null if sent by an AI agent instead of a human.</summary>
    public Guid? SentByUserId { get; }

    public Guid? AiRunId { get; }

    public MessageStatus Status { get; private set; }

    public string? ErrorCode { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// An inbound message starts <see cref="MessageStatus.Delivered"/> — it already
    /// arrived, by definition. <see cref="MessageStatus.Queued"/>/<see cref="MessageStatus.Sent"/>
    /// only make sense for the outbound send lifecycle (ORB-B05).
    /// </summary>
    public static Message Inbound(
        Guid tenantId,
        Guid conversationId,
        string externalId,
        string? body,
        string? mediaMime,
        string? replyToExternalId,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("External id is required.", nameof(externalId));
        }

        return new Message(
            Guid.NewGuid(),
            tenantId,
            conversationId,
            MessageDirection.Inbound,
            MessageCategory.Service,
            body,
            mediaKey: null,
            RequireLength(mediaMime, MediaMimeMaxLength, nameof(mediaMime)),
            RequireLength(externalId, ExternalIdMaxLength, nameof(externalId)),
            RequireLength(replyToExternalId, ExternalIdMaxLength, nameof(replyToExternalId)),
            templateId: null,
            templateVariablesJson: null,
            sentByUserId: null,
            aiRunId: null,
            MessageStatus.Delivered,
            sentAt: null,
            deliveredAt: now,
            readAt: null,
            now);
    }

    /// <summary>
    /// Starts <see cref="MessageStatus.Queued"/> — persist-first: the message exists
    /// before any attempt to actually send it (ORB-B05), so it's visible immediately and
    /// survives a crash between queueing and dispatch.
    /// </summary>
    public static Message OutboundText(
        Guid tenantId,
        Guid conversationId,
        string body,
        Guid? sentByUserId,
        MessageCategory category,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Body is required.", nameof(body));
        }

        return new Message(
            Guid.NewGuid(),
            tenantId,
            conversationId,
            MessageDirection.Outbound,
            category,
            body,
            mediaKey: null,
            mediaMime: null,
            externalId: null,
            replyToExternalId: null,
            templateId: null,
            templateVariablesJson: null,
            sentByUserId,
            aiRunId: null,
            MessageStatus.Queued,
            sentAt: null,
            deliveredAt: null,
            readAt: null,
            now);
    }

    /// <summary>
    /// Same persist-first contract as <see cref="OutboundText"/> — sent outside the
    /// service window with an approved template (ORB-B07). <paramref name="renderedBody"/>
    /// is already-substituted text; the template's own category becomes the message's.
    /// </summary>
    public static Message OutboundTemplate(
        Guid tenantId,
        Guid conversationId,
        Guid templateId,
        string renderedBody,
        IReadOnlyList<string> variables,
        MessageCategory category,
        Guid? sentByUserId,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new Message(
            Guid.NewGuid(),
            tenantId,
            conversationId,
            MessageDirection.Outbound,
            category,
            renderedBody,
            mediaKey: null,
            mediaMime: null,
            externalId: null,
            replyToExternalId: null,
            templateId,
            JsonSerializer.Serialize(variables),
            sentByUserId,
            aiRunId: null,
            MessageStatus.Queued,
            sentAt: null,
            deliveredAt: null,
            readAt: null,
            now);
    }

    /// <summary>Same persist-first contract as <see cref="OutboundText"/> (ORB-B06) — <paramref name="mediaKey"/> must already have been uploaded and validated as belonging to this tenant before this is called.</summary>
    public static Message OutboundMedia(
        Guid tenantId,
        Guid conversationId,
        string mediaKey,
        string mediaMime,
        string? caption,
        Guid? sentByUserId,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(mediaKey))
        {
            throw new ArgumentException("Media key is required.", nameof(mediaKey));
        }

        return new Message(
            Guid.NewGuid(),
            tenantId,
            conversationId,
            MessageDirection.Outbound,
            MessageCategory.Service,
            caption,
            mediaKey,
            RequireLength(mediaMime, MediaMimeMaxLength, nameof(mediaMime)),
            externalId: null,
            replyToExternalId: null,
            templateId: null,
            templateVariablesJson: null,
            sentByUserId,
            aiRunId: null,
            MessageStatus.Queued,
            sentAt: null,
            deliveredAt: null,
            readAt: null,
            now);
    }

    /// <summary>Meta accepted the send — <paramref name="externalId"/> is the wamid it assigned.</summary>
    public void MarkSent(string externalId, DateTimeOffset now)
    {
        ExternalId = RequireLength(externalId, ExternalIdMaxLength, nameof(externalId)) ?? throw new ArgumentException("External id is required.", nameof(externalId));
        Status = MessageStatus.Sent;
        SentAt = now;
    }

    /// <summary>Every retry attempt exhausted (or a non-transient error) — see MetaErrorCatalog.Describe for what counts as retryable.</summary>
    public void MarkFailed(string errorCode)
    {
        Status = MessageStatus.Failed;
        ErrorCode = RequireLength(errorCode, ErrorCodeMaxLength, nameof(errorCode));
    }

    /// <summary>Records where the downloaded media ended up once ORB-B06's processor has fetched it from Meta and stored it.</summary>
    public void MarkMediaStored(string mediaKey, string mediaMime)
    {
        MediaKey = string.IsNullOrWhiteSpace(mediaKey) ? throw new ArgumentException("Media key is required.", nameof(mediaKey)) : mediaKey;
        MediaMime = RequireLength(mediaMime, MediaMimeMaxLength, nameof(mediaMime));
    }

    private static string? RequireLength(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{paramName} must be at most {maxLength} characters.", paramName);
        }

        return trimmed;
    }
}
