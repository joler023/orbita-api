using Moq;
using Orbita.Application.Audit;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.UnitTests.Application;

public sealed class TeamMembersServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMembershipRepository> _memberships = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly TeamMembersService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    public TeamMembersServiceTests()
    {
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<Membership?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Membership?>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Membership>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<Membership>>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> query, CancellationToken ct) => query(ct));

        _sut = new TeamMembersService(_memberships.Object, _users.Object, _authorization.Object, _auditLogger.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task ListAsync_ReturnsActiveMembersWithUserDetails()
    {
        var owner = User.Create("owner@acme.com", "hash", "Owner Person", Now);
        var membership = Membership.CreateOwner(_tenantId, owner.Id, Now);
        _memberships.Setup(r => r.GetActiveByTenantAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Membership>)[membership]);
        _users.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<User>)[owner]);

        var result = await _sut.ListAsync(_tenantId, _callerId, CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal(owner.Email, summary.Email);
        Assert.Equal(MemberRole.Owner, summary.Role);
        Assert.False(summary.IsPending);
        _authorization.Verify(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ViewTeam, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_WhenCallerLacksPermission_PropagatesForbiddenWithoutQueryingMemberships()
    {
        _authorization
            .Setup(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ViewTeam, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("nope"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(_tenantId, _callerId, CancellationToken.None));

        _memberships.Verify(r => r.GetActiveByTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeRoleAsync_PromotingAnAgentToAdmin_UpdatesTheRole()
    {
        var agentUser = User.Create("agent@acme.com", "hash", "Agent Person", Now);
        var membership = Membership.CreateOwner(_tenantId, agentUser.Id, Now);
        membership.ChangeRole(MemberRole.Agent);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        _users.Setup(r => r.GetByIdAsync(agentUser.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agentUser);

        var summary = await _sut.ChangeRoleAsync(_tenantId, _callerId, membership.Id, MemberRole.Admin, CancellationToken.None);

        Assert.Equal(MemberRole.Admin, summary.Role);
        Assert.Equal(MemberRole.Admin, membership.Role);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _memberships.Verify(r => r.CountActiveByTenantAndRoleAsync(It.IsAny<Guid>(), It.IsAny<MemberRole>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditLogger.Verify(
            a => a.RecordAsync(_tenantId, _callerId, "membership.role_changed", nameof(Membership), membership.Id, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChangeRoleAsync_DemotingTheOnlyOwner_ThrowsCannotRemoveLastOwner()
    {
        var ownerUser = User.Create("owner@acme.com", "hash", "Owner Person", Now);
        var membership = Membership.CreateOwner(_tenantId, ownerUser.Id, Now);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        _memberships.Setup(r => r.CountActiveByTenantAndRoleAsync(_tenantId, MemberRole.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Assert.ThrowsAsync<CannotRemoveLastOwnerException>(
            () => _sut.ChangeRoleAsync(_tenantId, _callerId, membership.Id, MemberRole.Admin, CancellationToken.None));

        Assert.Equal(MemberRole.Owner, membership.Role);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeRoleAsync_DemotingOneOfSeveralOwners_Succeeds()
    {
        var ownerUser = User.Create("owner@acme.com", "hash", "Owner Person", Now);
        var membership = Membership.CreateOwner(_tenantId, ownerUser.Id, Now);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        _memberships.Setup(r => r.CountActiveByTenantAndRoleAsync(_tenantId, MemberRole.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _users.Setup(r => r.GetByIdAsync(ownerUser.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ownerUser);

        var summary = await _sut.ChangeRoleAsync(_tenantId, _callerId, membership.Id, MemberRole.Admin, CancellationToken.None);

        Assert.Equal(MemberRole.Admin, summary.Role);
    }

    [Fact]
    public async Task ChangeRoleAsync_WhenMembershipDoesNotExist_ThrowsMemberNotFound()
    {
        _memberships.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Membership?)null);

        await Assert.ThrowsAsync<MemberNotFoundException>(
            () => _sut.ChangeRoleAsync(_tenantId, _callerId, Guid.NewGuid(), MemberRole.Admin, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_RemovingTheOnlyOwner_ThrowsCannotRemoveLastOwner()
    {
        var membership = Membership.CreateOwner(_tenantId, Guid.NewGuid(), Now);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        _memberships.Setup(r => r.CountActiveByTenantAndRoleAsync(_tenantId, MemberRole.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Assert.ThrowsAsync<CannotRemoveLastOwnerException>(
            () => _sut.RemoveAsync(_tenantId, _callerId, membership.Id, CancellationToken.None));

        Assert.True(membership.IsActive);
    }

    [Fact]
    public async Task RemoveAsync_RemovingAnAgent_DeactivatesTheMembership()
    {
        var membership = Membership.Invite(_tenantId, Guid.NewGuid(), MemberRole.Agent, Guid.NewGuid(), Now);
        membership.Accept(Now);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);

        await _sut.RemoveAsync(_tenantId, _callerId, membership.Id, CancellationToken.None);

        Assert.False(membership.IsActive);
        _memberships.Verify(r => r.CountActiveByTenantAndRoleAsync(It.IsAny<Guid>(), It.IsAny<MemberRole>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditLogger.Verify(
            a => a.RecordAsync(_tenantId, _callerId, "membership.removed", nameof(Membership), membership.Id, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_WhenMembershipBelongsToAnotherTenant_ThrowsMemberNotFound()
    {
        var membership = Membership.CreateOwner(Guid.NewGuid(), Guid.NewGuid(), Now);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);

        await Assert.ThrowsAsync<MemberNotFoundException>(
            () => _sut.RemoveAsync(_tenantId, _callerId, membership.Id, CancellationToken.None));
    }
}
