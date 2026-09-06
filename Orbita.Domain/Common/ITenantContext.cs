namespace Orbita.Domain.Common;

/// <summary>
/// The tenant the current unit of work is acting on behalf of, resolved at runtime
/// (from a JWT claim once authentication exists, or set explicitly by a use case that
/// creates the tenant itself). Never hardcoded — see orbita-schema.dbml isolation rule.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
}
