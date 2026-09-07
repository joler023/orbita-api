using Orbita.Domain.Audit;

namespace Orbita.Application.Audit;

/// <summary>
/// Records an audit trail entry (ORB-A15) — call this from inside the same
/// application-service method whose <c>IUnitOfWork.SaveChangesAsync</c> call persists
/// the change being audited, so the entry and the change it describes land in the same
/// transaction. This only stages the entity via the repository; it never calls
/// SaveChangesAsync itself, on purpose — see <see cref="AuditLogger"/>.
/// </summary>
public interface IAuditLogger
{
    /// <param name="diff">Serialized to JSON as-is; pass an anonymous object, not a pre-serialized string.</param>
    Task RecordAsync(Guid tenantId, Guid actorUserId, string action, string entityType, Guid? entityId, object? diff, CancellationToken cancellationToken);

    /// <param name="diff">Serialized to JSON as-is; pass an anonymous object, not a pre-serialized string.</param>
    Task RecordSystemActionAsync(Guid tenantId, AuditActorType actorType, string action, string entityType, Guid? entityId, object? diff, CancellationToken cancellationToken);
}
