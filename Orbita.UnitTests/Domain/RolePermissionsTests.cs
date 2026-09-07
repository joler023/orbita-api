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
        [InlineData(MemberRole.Owner, Permission.ViewPipeline, true)]
        [InlineData(MemberRole.Owner, Permission.ManagePipeline, true)]
        [InlineData(MemberRole.Admin, Permission.ViewPipeline, true)]
        [InlineData(MemberRole.Admin, Permission.ManagePipeline, true)]
        [InlineData(MemberRole.Agent, Permission.ViewPipeline, true)]
        [InlineData(MemberRole.Agent, Permission.ManagePipeline, false)]
        [InlineData(MemberRole.Viewer, Permission.ViewPipeline, true)]
        [InlineData(MemberRole.Viewer, Permission.ManagePipeline, false)]
        [InlineData(MemberRole.Owner, Permission.ManageOpportunities, true)]
        [InlineData(MemberRole.Admin, Permission.ManageOpportunities, true)]
        [InlineData(MemberRole.Agent, Permission.ManageOpportunities, true)]
        [InlineData(MemberRole.Viewer, Permission.ManageOpportunities, false)]
        [InlineData(MemberRole.Owner, Permission.ViewContacts, true)]
        [InlineData(MemberRole.Owner, Permission.ManageContacts, true)]
        [InlineData(MemberRole.Admin, Permission.ViewContacts, true)]
        [InlineData(MemberRole.Admin, Permission.ManageContacts, true)]
        [InlineData(MemberRole.Agent, Permission.ViewContacts, true)]
        [InlineData(MemberRole.Agent, Permission.ManageContacts, true)]
        [InlineData(MemberRole.Viewer, Permission.ViewContacts, true)]
        [InlineData(MemberRole.Viewer, Permission.ManageContacts, false)]
    public void Grants_MatchesTheDocumentedPermissionMatrix(MemberRole role, Permission permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Grants(role, permission));
    }
}
