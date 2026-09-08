using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orbita.Application.Inbox;
using Orbita.Domain.Common;

namespace Orbita.Infrastructure.Workers;

/// <summary>
/// Drains the outbound send queue (ORB-B05) — same shape as InboundMessageWorker: claim
/// a batch on an outer scope, dispatch each job under its own tenant-scoped DI scope
/// (Contact/Conversation/ChannelAccount lookups inside the dispatch service need the
/// right ambient tenant for RLS), then persist outcomes once per batch.
/// </summary>
public sealed class OutboundMessageWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboundMessageWorker> logger)
    : PollingWorker(TimeSpan.FromSeconds(1), logger)
{
    private const int BatchSize = 20;

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var batch = await queue.DequeueBatchAsync(BatchSize, timeProvider.GetUtcNow(), cancellationToken);
        if (batch.Count == 0)
        {
            return;
        }

        foreach (var job in batch)
        {
            using var itemScope = scopeFactory.CreateScope();
            itemScope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(job.TenantId);
            var dispatchService = itemScope.ServiceProvider.GetRequiredService<IOutboundMessageDispatchService>();

            try
            {
                await dispatchService.DispatchAsync(job, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to dispatch outbound message job {JobId}.", job.Id);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
