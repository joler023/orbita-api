using System.Collections.Concurrent;
using Orbita.Application.Outbox;

namespace Orbita.IntegrationTests.TestSupport;

/// <summary>Records every envelope handed to it — registered by the fixture so a test can observe what the dispatcher actually published.</summary>
public sealed class RecordingIntegrationEventHandler : IIntegrationEventHandler
{
    private readonly ConcurrentBag<OutboxEnvelope> _received = [];

    public IReadOnlyCollection<OutboxEnvelope> Received => _received;

    public bool CanHandle(string eventType) => true;

    public Task HandleAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        _received.Add(envelope);
        return Task.CompletedTask;
    }
}
