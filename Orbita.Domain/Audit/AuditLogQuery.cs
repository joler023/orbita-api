namespace Orbita.Domain.Audit;

/// <summary>Optional filters for querying a tenant's audit log (ORB-A15's "consultable y filtrable").</summary>
public sealed record AuditLogQuery(
    string? EntityType = null,
    Guid? EntityId = null,
    Guid? ActorId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Limit = 50);
