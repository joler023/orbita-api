using Microsoft.EntityFrameworkCore;
using Orbita.Application.Inbox;
using Orbita.Domain.Inbox;

namespace Orbita.Infrastructure.Persistence.Repositories;

/// <summary>
/// Postgres stand-in for a real send queue (ORB-B05). Unlike the inbound webhook queue,
/// enqueueing here goes through the normal change tracker (<c>AddAsync</c>) — it needs
/// to land in the very same transaction as the <see cref="Message"/> and
/// <see cref="Conversation"/> changes it accompanies (persist-first), so raw SQL with
/// its own implicit transaction would be wrong here.
/// </summary>
public sealed class PostgresOutboundMessageQueue(OrbitaDbContext dbContext) : IOutboundMessageQueue
{
    public async Task EnqueueAsync(OutboundMessageJob job, CancellationToken cancellationToken)
        => await dbContext.OutboundMessageJobs.AddAsync(job, cancellationToken);

    public Task<IReadOnlyList<OutboundMessageJob>> DequeueBatchAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken)
        => QueryAsync(batchSize, now, cancellationToken);

    private async Task<IReadOnlyList<OutboundMessageJob>> QueryAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken)
        => await dbContext.OutboundMessageJobs
            .FromSqlInterpolated(
                $"""
                UPDATE outbound_message_jobs
                SET status = 'Processing'
                WHERE id IN (
                    SELECT id FROM outbound_message_jobs
                    WHERE status = 'Pending' AND next_attempt_at <= {now}
                    ORDER BY id
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING *
                """)
            .ToListAsync(cancellationToken);
}
