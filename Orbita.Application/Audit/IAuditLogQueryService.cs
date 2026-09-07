using Orbita.Domain.Audit;

namespace Orbita.Application.Audit;

/// <summary>Lets a tenant's Owner/Admin inspect their own audit trail (ORB-A15).</summary>
public interface IAuditLogQueryService
{
    /// <exception cref="Identity.ForbiddenException">Caller lacks the ViewAuditLog permission.</exception>
    Task<IReadOnlyList<AuditLogEntryDto>> QueryAsync(Guid tenantId, Guid callerUserId, AuditLogQuery query, CancellationToken cancellationToken);
}
