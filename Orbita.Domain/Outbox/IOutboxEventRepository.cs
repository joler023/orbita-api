namespace Orbita.Domain.Outbox;

public interface IOutboxEventRepository
{
    Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Claims up to <paramref name="batchSize"/> unpublished events whose exponential
    /// backoff window has elapsed (<c>occurred_at + 30s * 2^attempts &lt;= now</c>),
    /// skipping rows locked by a concurrent dispatch.
    /// </summary>
    Task<IReadOnlyList<OutboxEvent>> DequeuePendingAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken);
}
