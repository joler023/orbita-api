namespace Orbita.Domain.Audit;

/// <summary>
/// One append-only row in the audit trail (ORB-A15, orbita-schema.dbml's audit_log —
/// "Base para SOC 2 en la etapa 3"). Deliberately does not inherit
/// <see cref="Common.Entity"/>: that base class hardcodes a client-generated
/// <see cref="Guid"/> id, but the DBML specifies `id bigint [pk, increment]` for this
/// table specifically — a DB-assigned identity column, matching every other
/// append-only/log-shaped table in the schema. <see cref="Id"/> is 0 until EF Core
/// assigns the real value on save.
///
/// No mutator methods exist, on purpose: "sin UPDATE ni DELETE" (CLAUDE.md domain rule
/// 4) means genuinely immutable after construction, not just "nothing happens to call
/// the setters yet." A wrong fact gets a new compensating row, never a correction to
/// this one.
/// </summary>
public sealed class AuditLogEntry
{
    public const int ActionMaxLength = 120;
    public const int EntityTypeMaxLength = 80;

    private AuditLogEntry(
        Guid tenantId,
        Guid? actorId,
        AuditActorType actorType,
        string action,
        string entityType,
        Guid? entityId,
        string? diffJson,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        ActorId = actorId;
        ActorType = actorType;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        DiffJson = diffJson;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CreatedAt = createdAt;
    }

    public long Id { get; private set; }

    public Guid TenantId { get; }

    /// <summary>Null when <see cref="ActorType"/> is <see cref="AuditActorType.AiAgent"/> or <see cref="AuditActorType.System"/>.</summary>
    public Guid? ActorId { get; }

    public AuditActorType ActorType { get; }

    /// <summary>A dotted, lowercase verb phrase, e.g. "membership.role_changed".</summary>
    public string Action { get; }

    /// <summary>The kind of thing acted on, e.g. "Membership" — not a table name, a domain concept.</summary>
    public string EntityType { get; }

    public Guid? EntityId { get; }

    /// <summary>Raw JSON (stored as jsonb) — typically an old/new pair. Null when there's nothing to diff (e.g. a login event).</summary>
    public string? DiffJson { get; }

    public string? IpAddress { get; }

    public string? UserAgent { get; }

    public DateTimeOffset CreatedAt { get; }

    public static AuditLogEntry RecordForUser(
        Guid tenantId,
        Guid actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        string? diffJson,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now)
        => new(
            tenantId,
            actorUserId,
            AuditActorType.User,
            RequireLength(action, nameof(action), 1, ActionMaxLength),
            RequireLength(entityType, nameof(entityType), 1, EntityTypeMaxLength),
            entityId,
            diffJson,
            ipAddress,
            userAgent,
            now);

    public static AuditLogEntry RecordForSystem(
        Guid tenantId,
        AuditActorType actorType,
        string action,
        string entityType,
        Guid? entityId,
        string? diffJson,
        DateTimeOffset now)
    {
        if (actorType == AuditActorType.User)
        {
            throw new ArgumentException($"Use {nameof(RecordForUser)} for a {AuditActorType.User} actor.", nameof(actorType));
        }

        return new(
            tenantId,
            actorId: null,
            actorType,
            RequireLength(action, nameof(action), 1, ActionMaxLength),
            RequireLength(entityType, nameof(entityType), 1, EntityTypeMaxLength),
            entityId,
            diffJson,
            ipAddress: null,
            userAgent: null,
            now);
    }

    private static string RequireLength(string value, string paramName, int minLength, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            throw new ArgumentException(
                $"{paramName} must be between {minLength} and {maxLength} characters.",
                paramName);
        }

        return trimmed;
    }
}
