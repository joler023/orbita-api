using Orbita.Domain.Identity;

namespace Orbita.Application.Identity;

/// <summary>
/// Generalizes the "does this caller have permission X in this tenant" check
/// (ORB-A08). ORB-A07 originally inlined "must be Owner or Admin" directly inside
/// <see cref="ITeamInvitationService"/>'s implementation; any tenant-scoped,
/// authenticated action that varies by role should depend on this instead of
/// repeating that kind of check.
/// </summary>
public interface ITenantAuthorizationService
{
    /// <exception cref="ForbiddenException">
    /// The caller has no active, accepted membership in the tenant, or their role does
    /// not grant <paramref name="permission"/> — see <see cref="RolePermissions"/>.
    /// </exception>
    Task EnsurePermissionAsync(Guid tenantId, Guid callerUserId, Permission permission, CancellationToken cancellationToken);
}
