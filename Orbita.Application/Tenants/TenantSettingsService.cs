using Orbita.Application.Identity;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;

namespace Orbita.Application.Tenants;

public sealed class TenantSettingsService(
    ITenantRepository tenantRepository,
    ITenantAuthorizationService authorizationService,
    TimeProvider timeProvider) : ITenantSettingsService
{
    public async Task SetRequireMfaForMembersAsync(Guid tenantId, Guid callerUserId, bool required, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageSettings, cancellationToken);

        var tenant = await tenantRepository.GetByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found.");

        tenant.SetRequireMfaForMembers(required, timeProvider.GetUtcNow());
        await tenantRepository.SaveChangesAsync(cancellationToken);
    }
}
