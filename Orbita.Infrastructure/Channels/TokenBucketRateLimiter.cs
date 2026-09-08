using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Orbita.Application.Inbox;

namespace Orbita.Infrastructure.Channels;

/// <summary>
/// Caps outbound sends to 80/s per channel account (ORB-B05) — comfortably under
/// Meta's own per-number throughput tiers. One bucket per account, created lazily and
/// kept for the process's lifetime (accounts don't churn fast enough to justify eviction).
/// </summary>
public sealed class TokenBucketRateLimiter : IOutboundMessageRateLimiter, IDisposable
{
    private readonly ConcurrentDictionary<Guid, System.Threading.RateLimiting.TokenBucketRateLimiter> _limiters = new();

    public async Task AcquireAsync(Guid channelAccountId, CancellationToken cancellationToken)
    {
        var limiter = _limiters.GetOrAdd(channelAccountId, static _ => CreateLimiter());
        using var lease = await limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException("Failed to acquire a send slot from the rate limiter.");
        }
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
    }

    private static System.Threading.RateLimiting.TokenBucketRateLimiter CreateLimiter()
        => new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 80,
            TokensPerPeriod = 80,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = int.MaxValue,
            AutoReplenishment = true,
        });
}
