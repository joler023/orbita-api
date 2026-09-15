using Microsoft.EntityFrameworkCore;
using Orbita.Application.Channels;
using Orbita.Domain.Channels;
using Orbita.Infrastructure.Persistence;

namespace Orbita.Infrastructure.Channels;

/// <summary>
/// Postgres stand-in for SQS (ORB-B02). The insert is raw SQL rather than
/// <c>DbContext.Add</c>/<c>SaveChanges</c> specifically to get
/// <c>ON CONFLICT (payload_hash) DO NOTHING</c> — a duplicate payload must be a silent
/// no-op, not a unique-constraint exception, since Meta's own redeliveries are expected
/// traffic here.
/// </summary>
public sealed class PostgresInboundWebhookQueue(OrbitaDbContext dbContext, TimeProvider timeProvider) : IInboundWebhookQueue
{
    public Task EnqueueAsync(InboundWebhookEvent webhookEvent, CancellationToken cancellationToken)
        => dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO inbound_webhook_events
                (tenant_id, channel_account_id, kind, payload, payload_hash, received_at, status, attempts)
            VALUES
                ({webhookEvent.TenantId}, {webhookEvent.ChannelAccountId}, {webhookEvent.Kind.ToString()},
                 {webhookEvent.PayloadJson}::jsonb, {webhookEvent.PayloadHash}, {webhookEvent.ReceivedAt},
                 {webhookEvent.Status.ToString()}, {webhookEvent.Attempts})
            ON CONFLICT (payload_hash) DO NOTHING
            """,
            cancellationToken);

    public async Task<IReadOnlyList<InboundWebhookEvent>> DequeueBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await dbContext.InboundWebhookEvents
            .FromSqlInterpolated(
                $"""
                UPDATE inbound_webhook_events
                SET status = 'Processing', locked_at = {now}
                WHERE id IN (
                    SELECT id FROM inbound_webhook_events
                    WHERE status = 'Pending'
                    ORDER BY id
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING *
                """)
            .ToListAsync(cancellationToken);
    }

    public Task<int> RequeueStaleAsync(CancellationToken cancellationToken)
        => dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE inbound_webhook_events SET status = 'Pending', locked_at = NULL WHERE status = 'Processing'",
            cancellationToken);
}
