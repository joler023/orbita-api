namespace Orbita.Domain.Identity;

/// <summary>
/// A backend-enforced capability within a tenant (ORB-A08). Every tenant-scoped,
/// authenticated action that varies by role should check one of these through
/// <see cref="RolePermissions"/>, not invent its own ad-hoc role comparison.
/// </summary>
public enum Permission
{
    /// <summary>See the tenant's member and pending-invitation list.</summary>
    ViewTeam,

    /// <summary>Invite, resend, revoke, change the role of, or remove team members.</summary>
    ManageTeam,

    /// <summary>Change tenant-wide settings, e.g. requiring MFA for every member (ORB-A11).</summary>
    ManageSettings,
}
