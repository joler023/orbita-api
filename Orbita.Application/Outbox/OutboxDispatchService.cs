using Orbita.Domain.Common;
using Orbita.Domain.Outbox;

namespace Orbita.Application.Outbox;

public sealed class OutboxDispatchService(
    IOutboxEventRepository outboxEventRepository,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOutboxDispatchService
{
    public async Task DispatchOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        var pending = await outboxEventRepository.DequeuePendingAsync(batchSize, timeProvider.GetUtcNow(), cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var outboxEvent in pending)
        {
            try
            {
                await publisher.PublishAsync(ToEnvelope(outboxEvent), cancellationToken);
                outboxEvent.MarkPublished(timeProvider.GetUtcNow());
            }
            catch (Exception)
            {
                // A publish failure is this event's problem, not the batch's — it stays
                // pending for the next sweep (backoff grows with Attempts).
                outboxEvent.RegisterFailure();
            }
        }

        // No ambient tenant here on purpose: this dispatcher reads/writes across every
        // tenant's rows, and outbox_events has no RLS/query filter for exactly that reason.
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static OutboxEnvelope ToEnvelope(OutboxEvent outboxEvent)
        => new(outboxEvent.Id, outboxEvent.TenantId, outboxEvent.AggregateType, outboxEvent.AggregateId, outboxEvent.EventType, outboxEvent.PayloadJson, outboxEvent.TraceId, outboxEvent.OccurredAt);
}
