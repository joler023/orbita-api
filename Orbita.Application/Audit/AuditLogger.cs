using System.Text.Json;
using Orbita.Application.Common;
using Orbita.Domain.Audit;

namespace Orbita.Application.Audit;

public sealed class AuditLogger(
    IAuditLogRepository auditLogRepository,
    IRequestContext requestContext,
    TimeProvider timeProvider) : IAuditLogger
{
    public Task RecordAsync(Guid tenantId, Guid actorUserId, string action, string entityType, Guid? entityId, object? diff, CancellationToken cancellationToken)
    {
        var entry = AuditLogEntry.RecordForUser(
            tenantId,
            actorUserId,
            action,
            entityType,
            entityId,
            Serialize(diff),
            requestContext.IpAddress,
            requestContext.UserAgent,
            timeProvider.GetUtcNow());

        return auditLogRepository.AddAsync(entry, cancellationToken);
    }

    public Task RecordSystemActionAsync(
        Guid tenantId,
        AuditActorType actorType,
        string action,
        string entityType,
        Guid? entityId,
        object? diff,
        CancellationToken cancellationToken)
    {
        var entry = AuditLogEntry.RecordForSystem(tenantId, actorType, action, entityType, entityId, Serialize(diff), timeProvider.GetUtcNow());
        return auditLogRepository.AddAsync(entry, cancellationToken);
    }

    private static string? Serialize(object? diff) => diff is null ? null : JsonSerializer.Serialize(diff);
}
