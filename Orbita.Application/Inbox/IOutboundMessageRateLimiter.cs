namespace Orbita.Application.Inbox;

/// <summary>Caps outbound send throughput per channel account (ORB-B05) — Meta enforces its own per-number rate limits, this stays under them.</summary>
public interface IOutboundMessageRateLimiter
{
    /// <summary>Waits until a send slot for this account is available.</summary>
    Task AcquireAsync(Guid channelAccountId, CancellationToken cancellationToken);
}
