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
            // `power(2, attempts) - 1`, not `power(2, attempts)`: the backoff is for retries,
            // and with attempts = 0 the second form makes *every* event wait 30 seconds for
            // its first publish. Measured against a real database before the fix:
            // `message.received` took 38 seconds to reach its handler.
            //
            // That defeated the 500ms tick this worker was deliberately given, and made
            // ORB-C04's "latencia percibida por debajo de 6 segundos en el percentil 95"
            // arithmetically impossible — the assistant could not begin to think for half a
            // minute. Now: attempts 0 → immediately, 1 → 30s, 2 → 90s, 3 → 210s.
            .FromSqlInterpolated(
                $"""
                SELECT * FROM outbox_events
                WHERE published_at IS NULL
                  AND occurred_at + interval '30 seconds' * (power(2, attempts) - 1) <= {now}
                ORDER BY id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
}
