using Orbita.Application.Identity;
using Orbita.Domain.Audit;
using Orbita.Domain.Identity;

namespace Orbita.Application.Audit;

public sealed class AuditLogQueryService(
    IAuditLogRepository auditLogRepository,
    ITenantAuthorizationService authorizationService) : IAuditLogQueryService
{
    public async Task<IReadOnlyList<AuditLogEntryDto>> QueryAsync(
        Guid tenantId,
        Guid callerUserId,
        AuditLogQuery query,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewAuditLog, cancellationToken);

        var entries = await auditLogRepository.QueryAsync(tenantId, query, cancellationToken);
        return entries.Select(ToDto).ToList();
    }

    private static AuditLogEntryDto ToDto(AuditLogEntry entry)
        => new(entry.Id, entry.ActorId, entry.ActorType, entry.Action, entry.EntityType, entry.EntityId, entry.DiffJson, entry.CreatedAt);
}
