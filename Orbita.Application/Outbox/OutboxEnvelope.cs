namespace Orbita.Application.Outbox;

/// <summary>What an <see cref="IIntegrationEventHandler"/> actually sees — the outbox row's shape, decoupled from the entity itself.</summary>
public sealed record OutboxEnvelope(
    long Id,
    Guid TenantId,
    string AggregateType,
    Guid AggregateId,
    string EventType,
    string PayloadJson,
    string? TraceId,
    DateTimeOffset OccurredAt);
