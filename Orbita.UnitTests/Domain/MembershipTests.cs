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
        Assert.False(membership.IsPending);
    }

    [Fact]
    public void Invite_CreatesAPendingMembershipWithTheGivenRoleAndInviter()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var invitedBy = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var membership = Membership.Invite(tenantId, userId, MemberRole.Admin, invitedBy, now);

        Assert.Equal(MemberRole.Admin, membership.Role);
        Assert.Equal(invitedBy, membership.InvitedBy);
        Assert.Equal(now, membership.InvitedAt);
        Assert.Null(membership.AcceptedAt);
        Assert.True(membership.IsActive);
        Assert.True(membership.IsPending);
    }

    [Fact]
    public void Accept_SetsAcceptedAtAndClearsPending()
    {
        var membership = Membership.Invite(Guid.NewGuid(), Guid.NewGuid(), MemberRole.Agent, Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        var acceptedAt = DateTimeOffset.UnixEpoch.AddDays(1);

        membership.Accept(acceptedAt);

        Assert.Equal(acceptedAt, membership.AcceptedAt);
        Assert.False(membership.IsPending);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var membership = Membership.Invite(Guid.NewGuid(), Guid.NewGuid(), MemberRole.Agent, Guid.NewGuid(), DateTimeOffset.UnixEpoch);

        membership.Deactivate();

        Assert.False(membership.IsActive);
    }

    [Fact]
    public void ChangeRole_UpdatesTheRole()
    {
        var membership = Membership.CreateOwner(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch);

        membership.ChangeRole(MemberRole.Admin);

        Assert.Equal(MemberRole.Admin, membership.Role);
    }
}
