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
}
