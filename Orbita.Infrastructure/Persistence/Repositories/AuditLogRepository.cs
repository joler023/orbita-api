using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Audit;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class AuditLogRepository(OrbitaDbContext dbContext) : IAuditLogRepository
{
    public async Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken)
        => await dbContext.AuditLogEntries.AddAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(Guid tenantId, AuditLogQuery query, CancellationToken cancellationToken)
    {
        var entries = dbContext.AuditLogEntries.Where(e => e.TenantId == tenantId);

        if (query.EntityType is not null)
        {
            entries = entries.Where(e => e.EntityType == query.EntityType);
        }

        if (query.EntityId is not null)
        {
            entries = entries.Where(e => e.EntityId == query.EntityId);
        }

        if (query.ActorId is not null)
        {
            entries = entries.Where(e => e.ActorId == query.ActorId);
        }

        if (query.From is not null)
        {
            entries = entries.Where(e => e.CreatedAt >= query.From);
        }

        if (query.To is not null)
        {
            entries = entries.Where(e => e.CreatedAt <= query.To);
        }

        return await entries
            .OrderByDescending(e => e.CreatedAt)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
    }
}
