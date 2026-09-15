namespace Orbita.Application.Outbox;

/// <summary>One reaction to one or more outbox event types — e.g. ORB-B14's realtime publisher reacting to <c>message.*</c>/<c>conversation.*</c>.</summary>
public interface IIntegrationEventHandler
{
    bool CanHandle(string eventType);

    Task HandleAsync(OutboxEnvelope envelope, CancellationToken cancellationToken);
}
