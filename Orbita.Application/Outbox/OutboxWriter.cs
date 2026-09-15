using System.Text.Json;
using Orbita.Domain.Outbox;

namespace Orbita.Application.Outbox;

public sealed class OutboxWriter(IOutboxEventRepository outboxEventRepository, TimeProvider timeProvider) : IOutboxWriter
{
    public Task StageAsync(Guid tenantId, string aggregateType, Guid aggregateId, string eventType, object payload, CancellationToken cancellationToken)
    {
        var outboxEvent = OutboxEvent.Stage(tenantId, aggregateType, aggregateId, eventType, JsonSerializer.Serialize(payload), timeProvider.GetUtcNow());
        return outboxEventRepository.AddAsync(outboxEvent, cancellationToken);
    }
}
