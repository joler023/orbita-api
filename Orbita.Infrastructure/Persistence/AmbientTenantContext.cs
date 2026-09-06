using Orbita.Domain.Common;

namespace Orbita.Infrastructure.Persistence;

/// <summary>
/// Scoped (per-request/per-unit-of-work) holder of the acting tenant. Registered once
/// in DI and exposed behind both <see cref="ITenantContext"/> (read, consumed by the
/// EF Core global query filter) and <see cref="ITenantContextSetter"/> (write, used by
/// use cases that establish the tenant themselves, such as registration). Once
/// ORB-A06 lands, request middleware becomes the only caller of SetTenant, populating
/// it from the authenticated JWT's tenant claim.
/// </summary>
public sealed class AmbientTenantContext : ITenantContext, ITenantContextSetter
{
    public Guid? TenantId { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;
}
