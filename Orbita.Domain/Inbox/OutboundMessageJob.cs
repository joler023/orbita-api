namespace Orbita.Domain.Inbox;

/// <summary>
/// One send attempt lifecycle for one outbound <see cref="Message"/> (ORB-B05) — a
/// Postgres stand-in for a real send queue, same shape as
/// <see cref="Channels.InboundWebhookEvent"/>/<see cref="Outbox.OutboxEvent"/>: bigint
/// identity, does not inherit <see cref="Common.Entity"/>. Kept separate from
/// <see cref="Message"/> itself (rather than a status column driving a scan) because the
/// dispatcher needs to poll by <c>next_attempt_at</c> without ever touching the
/// partitioned, RLS'd messages table with no ambient tenant.
/// </summary>
public sealed class OutboundMessageJob
{
    public const int LastErrorMaxLength = 500;

    private OutboundMessageJob(
        Guid tenantId,
        Guid messageId,
        DateTimeOffset messageCreatedAt,
        Guid channelAccountId,
        DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        MessageId = messageId;
        MessageCreatedAt = messageCreatedAt;
        ChannelAccountId = channelAccountId;
        Status = OutboundJobStatus.Pending;
        Attempts = 0;
        NextAttemptAt = createdAt;
        CreatedAt = createdAt;
    }

    /// <summary>0 until Postgres assigns the real value on insert.</summary>
    public long Id { get; private set; }

    public Guid TenantId { get; }

    public Guid MessageId { get; }

    /// <summary>The other half of Message's composite (Id, CreatedAt) key — needed to join back to the partitioned table without a full scan.</summary>
    public DateTimeOffset MessageCreatedAt { get; }

    public Guid ChannelAccountId { get; }

    public OutboundJobStatus Status { get; private set; }

    public short Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static OutboundMessageJob Create(Guid tenantId, Guid messageId, DateTimeOffset messageCreatedAt, Guid channelAccountId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new OutboundMessageJob(tenantId, messageId, messageCreatedAt, channelAccountId, now);
    }

    public void MarkProcessing() => Status = OutboundJobStatus.Processing;

    public void MarkSent() => Status = OutboundJobStatus.Sent;

    public void MarkFailed(string error)
    {
        Status = OutboundJobStatus.Failed;
        LastError = Truncate(error);
    }

    public void ScheduleRetry(DateTimeOffset now, TimeSpan backoff, string? error)
    {
        Attempts++;
        Status = OutboundJobStatus.Pending;
        NextAttemptAt = now + backoff;
        LastError = Truncate(error);
    }

    private static string? Truncate(string? error)
        => string.IsNullOrEmpty(error) ? error : error[..Math.Min(error.Length, LastErrorMaxLength)];
}
