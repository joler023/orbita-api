using System.Collections.Frozen;

namespace Orbita.Domain.Identity;

/// <summary>
/// The documented permission matrix for the four roles in orbita-schema.dbml's
/// member_role enum (ORB-A08). This is the single source of truth for "can this role
/// do X" — application services ask <see cref="Grants"/> instead of comparing
/// <see cref="MemberRole"/> values inline, which is what let ORB-A07's "Owner or
/// Admin" check collapse into one line here instead of being copy-pasted per feature.
/// </summary>
public static class RolePermissions
{
    private static readonly FrozenDictionary<MemberRole, FrozenSet<Permission>> ByRole = new Dictionary<MemberRole, FrozenSet<Permission>>
    {
        [MemberRole.Owner] = new HashSet<Permission> { Permission.ViewTeam, Permission.ManageTeam, Permission.ViewAuditLog }.ToFrozenSet(),
        [MemberRole.Admin] = new HashSet<Permission> { Permission.ViewTeam, Permission.ManageTeam, Permission.ViewAuditLog }.ToFrozenSet(),
        [MemberRole.Owner] = new HashSet<Permission> { Permission.ViewTeam, Permission.ManageTeam, Permission.ManageBilling }.ToFrozenSet(),
        [MemberRole.Admin] = new HashSet<Permission> { Permission.ViewTeam, Permission.ManageTeam }.ToFrozenSet(),
        [MemberRole.Owner] = new HashSet<Permission> { Permission.ViewTeam, Permission.ManageTeam, Permission.ManageSettings }.ToFrozenSet(),
        [MemberRole.Admin] = new HashSet<Permission> { Permission.ViewTeam, Permission.ManageTeam, Permission.ManageSettings }.ToFrozenSet(),
        [MemberRole.Agent] = new HashSet<Permission> { Permission.ViewTeam }.ToFrozenSet(),
        [MemberRole.Viewer] = new HashSet<Permission> { Permission.ViewTeam }.ToFrozenSet(),
    }.ToFrozenDictionary();

    public static bool Grants(MemberRole role, Permission permission) => ByRole[role].Contains(permission);
}
