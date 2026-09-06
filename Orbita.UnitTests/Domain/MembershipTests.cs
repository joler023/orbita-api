using Orbita.Domain.Identity;

namespace Orbita.UnitTests.Domain;

public sealed class MembershipTests
{
    [Fact]
    public void CreateOwner_SetsOwnerRoleAndSelfAccepts()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var membership = Membership.CreateOwner(tenantId, userId, now);

        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal(userId, membership.UserId);
        Assert.Equal(MemberRole.Owner, membership.Role);
        Assert.Null(membership.InvitedBy);
        Assert.Null(membership.InvitedAt);
        Assert.Equal(now, membership.AcceptedAt);
        Assert.True(membership.IsActive);
        Assert.Equal(now, membership.CreatedAt);
    }
}
