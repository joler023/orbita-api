namespace Orbita.Domain.Audit;

/// <summary>Matches orbita-schema.dbml's audit_log.actor_type ('user' | 'ai_agent' | 'system').</summary>
public enum AuditActorType
{
    User,
    AiAgent,
    System,
}
