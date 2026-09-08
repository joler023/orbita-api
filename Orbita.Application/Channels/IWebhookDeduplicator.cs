namespace Orbita.Application.Channels;

/// <summary>
/// Single-instance fast-path guard against re-enqueueing a payload Meta redelivered
/// before we answered 200 (ORB-B02). Not the durable idempotency mechanism — that's the
/// unique index on <c>inbound_webhook_events.payload_hash</c>, which is what actually
/// protects against a duplicate landing on a different instance.
/// </summary>
public interface IWebhookDeduplicator
{
    /// <returns>True the first time <paramref name="payloadHash"/> is seen; false if already seen.</returns>
    Task<bool> TryMarkSeenAsync(string payloadHash, CancellationToken cancellationToken);
}
