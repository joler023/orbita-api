namespace Orbita.Domain.Audit;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken);

    /// <summary>Newest first, filtered by <paramref name="tenantId"/> and whatever <paramref name="query"/> specifies.</summary>
    Task<IReadOnlyList<AuditLogEntry>> QueryAsync(Guid tenantId, AuditLogQuery query, CancellationToken cancellationToken);
}
