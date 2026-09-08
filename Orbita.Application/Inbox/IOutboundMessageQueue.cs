using Orbita.Domain.Inbox;

namespace Orbita.Application.Inbox;

/// <summary>Durable send queue for outbound messages (ORB-B05) — a Postgres stand-in, same category as <see cref="Channels.IInboundWebhookQueue"/>.</summary>
public interface IOutboundMessageQueue
{
    Task EnqueueAsync(OutboundMessageJob job, CancellationToken cancellationToken);

    /// <summary>Claims up to <paramref name="batchSize"/> jobs whose <c>NextAttemptAt</c> has passed, skipping rows locked by a concurrent dispatch.</summary>
    Task<IReadOnlyList<OutboundMessageJob>> DequeueBatchAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken);
}
