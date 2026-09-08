using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Outbox;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class OutboxEventRepository(OrbitaDbContext dbContext) : IOutboxEventRepository
{
    public async Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
        => await dbContext.OutboxEvents.AddAsync(outboxEvent, cancellationToken);

    /// <remarks>
    /// The FOR UPDATE SKIP LOCKED row lock is only actually held for this one
    /// statement's own implicit transaction — it protects against this same dispatcher
    /// racing itself across ticks, but not yet against two horizontally-scaled dispatcher
    /// instances both claiming a row in the same instant (that needs this SELECT and the
    /// later SaveChangesAsync sharing one explicit transaction, which nothing here does
    /// yet). Fine for a single dispatcher instance; revisit before scaling it out.
    /// </remarks>
    public Task<IReadOnlyList<OutboxEvent>> DequeuePendingAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken)
        => QueryAsync(batchSize, now, cancellationToken);

    private async Task<IReadOnlyList<OutboxEvent>> QueryAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken)
        => await dbContext.OutboxEvents
            .FromSqlInterpolated(
                $"""
                SELECT * FROM outbox_events
                WHERE published_at IS NULL
                  AND occurred_at + interval '30 seconds' * power(2, attempts) <= {now}
                ORDER BY id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
}
