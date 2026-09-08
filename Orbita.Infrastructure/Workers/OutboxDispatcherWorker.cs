using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orbita.Application.Outbox;

namespace Orbita.Infrastructure.Workers;

/// <summary>
/// 500ms sweep of the outbox (ORB-B04) — the interval ORB-B14 later documents as this
/// system's realtime-update latency. Each tick runs in its own scope, same as every
/// other worker here.
/// </summary>
public sealed class OutboxDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDispatcherWorker> logger)
    : PollingWorker(TimeSpan.FromMilliseconds(500), logger)
{
    private const int BatchSize = 50;

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dispatchService = scope.ServiceProvider.GetRequiredService<IOutboxDispatchService>();
        await dispatchService.DispatchOnceAsync(BatchSize, cancellationToken);
    }
}
