namespace Orbita.Domain.Common;

/// <summary>
/// Write side of <see cref="ITenantContext"/>. Used by use cases that establish which
/// tenant a unit of work belongs to before any tenant-scoped entity is persisted —
/// today that is organization registration; once ORB-A06 lands, request middleware
/// will call this from the authenticated JWT's tenant claim instead.
/// </summary>
public interface ITenantContextSetter
{
    void SetTenant(Guid tenantId);
}
