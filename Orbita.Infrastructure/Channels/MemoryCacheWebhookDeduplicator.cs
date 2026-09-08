using Microsoft.Extensions.Caching.Memory;
using Orbita.Application.Channels;

namespace Orbita.Infrastructure.Channels;

/// <summary>
/// In-memory de-dup cache (ORB-B02). Racy under concurrency by design — two requests for
/// the same hash can both pass — because it's only a fast path against Meta's own
/// quick retries on a single instance; the durable, actually-correct idempotency comes
/// from the unique index on <c>inbound_webhook_events.payload_hash</c>
/// (<see cref="PostgresInboundWebhookQueue"/>'s ON CONFLICT DO NOTHING), which is what
/// still catches a duplicate that lands on a different instance or after this entry
/// expires.
/// </summary>
public sealed class MemoryCacheWebhookDeduplicator(IMemoryCache cache) : IWebhookDeduplicator
{
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromMinutes(10);

    public Task<bool> TryMarkSeenAsync(string payloadHash, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(payloadHash, out _))
        {
            return Task.FromResult(false);
        }

        cache.Set(payloadHash, true, RetentionWindow);
        return Task.FromResult(true);
    }
}
