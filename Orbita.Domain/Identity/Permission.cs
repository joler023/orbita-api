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

    /// <summary>Query the tenant's audit log (ORB-A15).</summary>
    ViewAuditLog,
    /// <summary>Subscribe to, change, or cancel the tenant's plan; view invoices (ORB-A12).</summary>
    ManageBilling,
    /// <summary>Change tenant-wide settings, e.g. requiring MFA for every member (ORB-A11).</summary>
    ManageSettings,

    /// <summary>See the tenant's connected channels and their status (ORB-B01).</summary>
    ViewChannels,

    /// <summary>Connect, re-verify, or disconnect a channel account (ORB-B01).</summary>
    ManageChannels,

    /// <summary>
    /// Configure the tenant's AI assistants and the documents they answer from
    /// (ORB-C02/C10). Owner and Admin, like <see cref="ManageSettings"/> — an agent's
    /// instructions and knowledge shape what customers are told, so it is not something
    /// a Viewer or an Agent changes.
    /// </summary>
    ManageAiAgents,
}
