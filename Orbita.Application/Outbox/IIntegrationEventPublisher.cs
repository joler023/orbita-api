namespace Orbita.Application.Outbox;

/// <summary>
/// Fans an outbox event out to whatever's listening (in-process handlers today; a real
/// broker later without changing <see cref="IOutboxDispatchService"/>).
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken);
}
