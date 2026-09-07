using Moq;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.UnitTests.Application;

public sealed class TenantAuthorizationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMembershipRepository> _memberships = new();
    private readonly Mock<ITenantContextSetter> _tenantContextSetter = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly TenantAuthorizationService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public TenantAuthorizationServiceTests()
    {
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<Membership?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Membership?>> query, CancellationToken ct) => query(ct));

        _sut = new TenantAuthorizationService(_memberships.Object, _tenantContextSetter.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task EnsurePermissionAsync_WhenRoleGrantsThePermission_DoesNotThrowAndSetsTenantContext()
    {
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.CreateOwner(_tenantId, _userId, Now));

        await _sut.EnsurePermissionAsync(_tenantId, _userId, Permission.ManageTeam, CancellationToken.None);

        _tenantContextSetter.Verify(s => s.SetTenant(_tenantId), Times.Once);
    }

    [Fact]
    public async Task EnsurePermissionAsync_WhenRoleDoesNotGrantThePermission_ThrowsForbidden()
    {
        var membership = Membership.Invite(_tenantId, _userId, MemberRole.Agent, Guid.NewGuid(), Now);
        membership.Accept(Now);
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(membership);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.EnsurePermissionAsync(_tenantId, _userId, Permission.ManageTeam, CancellationToken.None));
    }

    [Fact]
    public async Task EnsurePermissionAsync_WhenCallerHasNoMembership_ThrowsForbidden()
    {
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((Membership?)null);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.EnsurePermissionAsync(_tenantId, _userId, Permission.ViewTeam, CancellationToken.None));
    }

    [Fact]
    public async Task EnsurePermissionAsync_WhenMembershipIsStillPending_ThrowsForbidden()
    {
        var membership = Membership.Invite(_tenantId, _userId, MemberRole.Owner, Guid.NewGuid(), Now);
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(membership);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.EnsurePermissionAsync(_tenantId, _userId, Permission.ViewTeam, CancellationToken.None));
    }

    [Fact]
    public async Task EnsurePermissionAsync_WhenMembershipIsDeactivated_ThrowsForbidden()
    {
        var membership = Membership.CreateOwner(_tenantId, _userId, Now);
        membership.Deactivate();
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(membership);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.EnsurePermissionAsync(_tenantId, _userId, Permission.ViewTeam, CancellationToken.None));
    }
}
