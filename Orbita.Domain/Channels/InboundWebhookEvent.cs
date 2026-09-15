using System.Security.Cryptography;

namespace Orbita.Domain.Channels;

/// <summary>
/// One durable row per accepted inbound webhook payload (ORB-B02) — a Postgres
/// stand-in for an SQS queue while there is no Lambda yet. Mirrors
/// <see cref="Audit.AuditLogEntry"/>: does NOT inherit <see cref="Common.Entity"/>
/// because <see cref="Id"/> is a DB-assigned bigint identity, not a client-generated
/// Guid. Every accepted webhook lands here exactly once before anything tries to
/// interpret its payload — the no-loss contract for the ingestion boundary; ORB-B03 is
/// what actually reads <see cref="PayloadJson"/> and turns it into messages.
/// </summary>
public sealed class InboundWebhookEvent
{
    public const int PayloadHashLength = 64;
    public const short DefaultMaxAttempts = 5;

    private InboundWebhookEvent(
        Guid tenantId,
        Guid channelAccountId,
        ChannelKind kind,
        string payloadJson,
        string payloadHash,
        DateTimeOffset receivedAt)
    {
        TenantId = tenantId;
        ChannelAccountId = channelAccountId;
        Kind = kind;
        PayloadJson = payloadJson;
        PayloadHash = payloadHash;
        ReceivedAt = receivedAt;
        Status = InboundWebhookStatus.Pending;
        Attempts = 0;
    }

    /// <summary>0 until Postgres assigns the real value on insert.</summary>
    public long Id { get; private set; }

    public Guid TenantId { get; }

    public Guid ChannelAccountId { get; }

    public ChannelKind Kind { get; }

    public string PayloadJson { get; }

    /// <summary>Lowercase hex SHA-256 of the raw request body — the de-dup and idempotency key.</summary>
    public string PayloadHash { get; }

    public DateTimeOffset ReceivedAt { get; }

    public InboundWebhookStatus Status { get; private set; }

    public short Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? LockedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public static InboundWebhookEvent Receive(
        Guid tenantId,
        Guid channelAccountId,
        ChannelKind kind,
        string payloadJson,
        string payloadHash,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (channelAccountId == Guid.Empty)
        {
            throw new ArgumentException("Channel account id is required.", nameof(channelAccountId));
        }

        if (string.IsNullOrEmpty(payloadJson))
        {
            throw new ArgumentException("Payload is required.", nameof(payloadJson));
        }

        if (payloadHash.Length != PayloadHashLength)
        {
            throw new ArgumentException($"Payload hash must be {PayloadHashLength} characters.", nameof(payloadHash));
        }

        return new InboundWebhookEvent(tenantId, channelAccountId, kind, payloadJson, payloadHash, now);
    }

    /// <summary>SHA-256 of the raw body, as the lowercase hex string stored in <see cref="PayloadHash"/>.</summary>
    public static string Hash(ReadOnlySpan<byte> rawBody)
        => Convert.ToHexString(SHA256.HashData(rawBody)).ToLowerInvariant();

    public void MarkProcessing(DateTimeOffset now)
    {
        Status = InboundWebhookStatus.Processing;
        LockedAt = now;
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = InboundWebhookStatus.Processed;
        ProcessedAt = now;
        LockedAt = null;
    }

    /// <summary>Reached <paramref name="maxAttempts"/> → <see cref="InboundWebhookStatus.Dead"/>; otherwise back to Pending for the next sweep.</summary>
    public void MarkFailed(string error, DateTimeOffset now, short maxAttempts = DefaultMaxAttempts)
    {
        Attempts++;
        LastError = error;
        LockedAt = null;
        Status = Attempts >= maxAttempts ? InboundWebhookStatus.Dead : InboundWebhookStatus.Pending;
    }

    public void Requeue()
    {
        Status = InboundWebhookStatus.Pending;
        LockedAt = null;
    }
}
