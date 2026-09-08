using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orbita.Application.Channels;
using Orbita.Application.Inbox;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;

namespace Orbita.Infrastructure.Workers;

/// <summary>
/// Drains the inbound webhook queue (ORB-B03): claims a batch, processes each event
/// under its own tenant-scoped DI scope (query filters on Contact/Conversation/Message
/// are ambient-tenant-based, so a shared scope across tenants would be wrong), then
/// records the outcome on the original queue rows in one save.
/// </summary>
public sealed class InboundMessageWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<InboundMessageWorker> logger)
    : PollingWorker(TimeSpan.FromSeconds(2), logger)
{
    private const int BatchSize = 20;
    private const short MaxAttempts = InboundWebhookEvent.DefaultMaxAttempts;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IInboundWebhookQueue>();
        var recovered = await queue.RequeueStaleAsync(cancellationToken);
        if (recovered > 0)
        {
            logger.LogWarning("Requeued {Count} inbound webhook event(s) left Processing by a previous run.", recovered);
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IInboundWebhookQueue>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var batch = await queue.DequeueBatchAsync(BatchSize, cancellationToken);
        if (batch.Count == 0)
        {
            return;
        }

        foreach (var webhookEvent in batch)
        {
            await ProcessOneAsync(webhookEvent, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessOneAsync(InboundWebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        try
        {
            using var itemScope = scopeFactory.CreateScope();
            itemScope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(webhookEvent.TenantId);
            var processor = itemScope.ServiceProvider.GetRequiredService<IInboundMessageProcessor>();

            await processor.ProcessAsync(webhookEvent, cancellationToken);

            webhookEvent.MarkProcessed(timeProvider.GetUtcNow());
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to process inbound webhook event {EventId}.", webhookEvent.Id);
            webhookEvent.MarkFailed(exception.Message, timeProvider.GetUtcNow(), MaxAttempts);
        }
    }
}
