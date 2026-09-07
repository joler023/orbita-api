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
    public void Grants_MatchesTheDocumentedPermissionMatrix(MemberRole role, Permission permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Grants(role, permission));
    }
}
