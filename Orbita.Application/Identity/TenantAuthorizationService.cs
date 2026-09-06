using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Identity;

public sealed class TenantAuthorizationService(
    IMembershipRepository membershipRepository,
    ITenantContextSetter tenantContextSetter,
    IUnitOfWork unitOfWork) : ITenantAuthorizationService
{
    public async Task EnsurePermissionAsync(Guid tenantId, Guid callerUserId, Permission permission, CancellationToken cancellationToken)
    {
        tenantContextSetter.SetTenant(tenantId);
        var caller = await unitOfWork.QueryInTenantScopeAsync(
            ct => membershipRepository.GetByTenantAndUserAsync(tenantId, callerUserId, ct),
            cancellationToken);

        if (caller is null || !caller.IsActive || caller.IsPending || !RolePermissions.Grants(caller.Role, permission))
        {
            throw new ForbiddenException($"Caller does not have the '{permission}' permission in this tenant.");
        }
    }
}
