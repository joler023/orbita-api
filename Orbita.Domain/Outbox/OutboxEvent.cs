using System.Diagnostics;

namespace Orbita.Domain.Outbox;

/// <summary>
/// One staged integration event (ORB-B04) — written in the same transaction as the
/// domain change it describes (see <see cref="Application.Outbox.IOutboxWriter"/>,
/// same "stage now, the caller's own SaveChangesAsync persists it" pattern as
/// <see cref="Application.Audit.IAuditLogger"/>), then published asynchronously by
/// <see cref="Application.Outbox.IOutboxDispatchService"/>. Does not inherit
/// <see cref="Common.Entity"/> — same reason as <see cref="Audit.AuditLogEntry"/> and
/// <see cref="Channels.InboundWebhookEvent"/>: <see cref="Id"/> is a DB-assigned bigint identity.
/// </summary>
public sealed class OutboxEvent
{
    public const int AggregateTypeMaxLength = 80;
    public const int EventTypeMaxLength = 120;

    private OutboxEvent(
        Guid tenantId,
        string aggregateType,
        Guid aggregateId,
        string eventType,
        string payloadJson,
        string? traceId,
        DateTimeOffset occurredAt)
    {
        TenantId = tenantId;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        EventType = eventType;
        PayloadJson = payloadJson;
        TraceId = traceId;
        OccurredAt = occurredAt;
        Attempts = 0;
    }

    /// <summary>0 until Postgres assigns the real value on insert.</summary>
    public long Id { get; private set; }

    public Guid TenantId { get; }

    /// <summary>The kind of aggregate this event is about, e.g. "Conversation" — a domain concept, not a table name.</summary>
    public string AggregateType { get; }

    public Guid AggregateId { get; }

    /// <summary>A dotted, lowercase verb phrase, e.g. "message.received".</summary>
    public string EventType { get; }

    /// <summary>No PII — ids and enums only. See CLAUDE.md's outbox section for why.</summary>
    public string PayloadJson { get; }

    public string? TraceId { get; }

    public DateTimeOffset OccurredAt { get; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public int Attempts { get; private set; }

    public static OutboxEvent Stage(
        Guid tenantId,
        string aggregateType,
        Guid aggregateId,
        string eventType,
        string payloadJson,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new OutboxEvent(
            tenantId,
            RequireLength(aggregateType, AggregateTypeMaxLength, nameof(aggregateType)),
            aggregateId,
            RequireLength(eventType, EventTypeMaxLength, nameof(eventType)),
            payloadJson,
            Activity.Current?.TraceId.ToString(),
            now);
    }

    public void MarkPublished(DateTimeOffset now) => PublishedAt = now;

    public void RegisterFailure() => Attempts++;

    private static string RequireLength(string value, int maxLength, string paramName)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{paramName} must be between 1 and {maxLength} characters.", paramName);
        }

        return trimmed;
    }
}
