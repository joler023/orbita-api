using Orbita.Domain.Audit;

namespace Orbita.Application.Audit;

public sealed record AuditLogEntryDto(
    long Id,
    Guid? ActorId,
    AuditActorType ActorType,
    string Action,
    string EntityType,
    Guid? EntityId,
    string? DiffJson,
    DateTimeOffset CreatedAt);
