using Orbita.Domain.Identity;

namespace Orbita.UnitTests.Domain;

public sealed class RolePermissionsTests
{
    [Theory]
    [InlineData(MemberRole.Owner, Permission.ViewTeam, true)]
    [InlineData(MemberRole.Owner, Permission.ManageTeam, true)]
    [InlineData(MemberRole.Admin, Permission.ViewTeam, true)]
    [InlineData(MemberRole.Admin, Permission.ManageTeam, true)]
    [InlineData(MemberRole.Agent, Permission.ViewTeam, true)]
    [InlineData(MemberRole.Agent, Permission.ManageTeam, false)]
    [InlineData(MemberRole.Viewer, Permission.ViewTeam, true)]
    [InlineData(MemberRole.Viewer, Permission.ManageTeam, false)]
    [InlineData(MemberRole.Owner, Permission.ViewAuditLog, true)]
    [InlineData(MemberRole.Admin, Permission.ViewAuditLog, true)]
    [InlineData(MemberRole.Agent, Permission.ViewAuditLog, false)]
    [InlineData(MemberRole.Viewer, Permission.ViewAuditLog, false)]
    [InlineData(MemberRole.Owner, Permission.ManageBilling, true)]
    [InlineData(MemberRole.Admin, Permission.ManageBilling, false)]
    [InlineData(MemberRole.Agent, Permission.ManageBilling, false)]
    [InlineData(MemberRole.Viewer, Permission.ManageBilling, false)]
    [InlineData(MemberRole.Owner, Permission.ManageSettings, true)]
    [InlineData(MemberRole.Admin, Permission.ManageSettings, true)]
    [InlineData(MemberRole.Agent, Permission.ManageSettings, false)]
    [InlineData(MemberRole.Viewer, Permission.ManageSettings, false)]
    [InlineData(MemberRole.Owner, Permission.ViewChannels, true)]
    [InlineData(MemberRole.Agent, Permission.ViewChannels, true)]
    [InlineData(MemberRole.Viewer, Permission.ViewChannels, true)]
    [InlineData(MemberRole.Owner, Permission.ManageChannels, true)]
    [InlineData(MemberRole.Admin, Permission.ManageChannels, true)]
    [InlineData(MemberRole.Agent, Permission.ManageChannels, false)]
    [InlineData(MemberRole.Viewer, Permission.ManageChannels, false)]
    public void Grants_MatchesTheDocumentedPermissionMatrix(MemberRole role, Permission permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Grants(role, permission));
    }
}
