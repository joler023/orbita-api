using Orbita.Domain.Channels;

namespace Orbita.Application.Channels;

/// <summary>
/// The durable queue an accepted webhook lands on (ORB-B02) — a Postgres stand-in for
/// SQS. Ports + local stand-ins throughout Channels, no AWS SDK yet.
/// </summary>
public interface IInboundWebhookQueue
{
    /// <summary>Idempotent on <see cref="InboundWebhookEvent.PayloadHash"/>: enqueueing the same payload twice is a no-op, not an error.</summary>
    Task EnqueueAsync(InboundWebhookEvent webhookEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> pending events (marking them
    /// Processing) for this worker instance alone — concurrent workers never claim the
    /// same row (ORB-B03).
    /// </summary>
    Task<IReadOnlyList<InboundWebhookEvent>> DequeueBatchAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>Resets anything left Processing (e.g. by a worker that crashed mid-batch) back to Pending, so it gets picked up again.</summary>
    Task<int> RequeueStaleAsync(CancellationToken cancellationToken);
}
