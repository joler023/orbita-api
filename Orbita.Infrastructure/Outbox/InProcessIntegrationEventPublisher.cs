using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orbita.Application.Outbox;

namespace Orbita.Infrastructure.Outbox;

/// <summary>
/// In-process stand-in for a real broker (ORB-B04): fans an event out to every
/// registered <see cref="IIntegrationEventHandler"/> in a fresh DI scope. One handler's
/// exception is logged and never blocks the others, and never fails the publish itself
/// — a handler failing is that handler's problem to retry on its own terms, not a
/// reason to keep redelivering the event to handlers that already succeeded.
/// </summary>
public sealed class InProcessIntegrationEventPublisher(
    IServiceScopeFactory scopeFactory,
    ILogger<InProcessIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    public async Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Publishing {EventType} for {AggregateType}:{AggregateId}.",
            envelope.EventType, envelope.AggregateType, envelope.AggregateId);

        using var scope = scopeFactory.CreateScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IIntegrationEventHandler>())
        {
            if (!handler.CanHandle(envelope.EventType))
            {
                continue;
            }

            try
            {
                await handler.HandleAsync(envelope, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Integration event handler {Handler} failed for {EventType}.", handler.GetType().Name, envelope.EventType);
            }
        }
    }
}
