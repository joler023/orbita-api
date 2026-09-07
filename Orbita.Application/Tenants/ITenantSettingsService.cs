namespace Orbita.Application.Tenants;

/// <summary>Tenant-wide settings an Owner/Admin can configure (ORB-A11 adds the first one).</summary>
public interface ITenantSettingsService
{
    /// <exception cref="Identity.ForbiddenException">Caller lacks the ManageSettings permission.</exception>
    Task SetRequireMfaForMembersAsync(Guid tenantId, Guid callerUserId, bool required, CancellationToken cancellationToken);
}
