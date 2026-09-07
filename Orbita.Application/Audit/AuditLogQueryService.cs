using Orbita.Application.Identity;
using Orbita.Domain.Audit;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Audit;

public sealed class AuditLogQueryService(
    IAuditLogRepository auditLogRepository,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork) : IAuditLogQueryService
{
    public async Task<IReadOnlyList<AuditLogEntryDto>> QueryAsync(
        Guid tenantId,
        Guid callerUserId,
        AuditLogQuery query,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewAuditLog, cancellationToken);

        // audit_log is RLS'd like most tenant-scoped tables (see
        // AuditLogEntryConfiguration) — EnsurePermissionAsync's own tenant-scoped
        // query already closed its transaction by the time it returns, so the read
        // here needs its own QueryInTenantScopeAsync to re-establish app.tenant_id
        // for this query; SET LOCAL doesn't outlive the transaction that set it.
        var entries = await unitOfWork.QueryInTenantScopeAsync(
            ct => auditLogRepository.QueryAsync(tenantId, query, ct),
            cancellationToken);

        return entries.Select(ToDto).ToList();
    }

    private static AuditLogEntryDto ToDto(AuditLogEntry entry)
        => new(entry.Id, entry.ActorId, entry.ActorType, entry.Action, entry.EntityType, entry.EntityId, entry.DiffJson, entry.CreatedAt);
}
