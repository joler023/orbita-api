using Moq;
using Orbita.Application.Identity;
using Orbita.Application.Tenants;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class TenantSettingsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly TenantSettingsService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    public TenantSettingsServiceTests()
    {
        _sut = new TenantSettingsService(_tenants.Object, _authorization.Object, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task SetRequireMfaForMembersAsync_WhenAuthorized_UpdatesTheTenant()
    {
        var tenant = Tenant.Create("acme", "Acme", now: DateTimeOffset.UnixEpoch);
        _tenants.Setup(r => r.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        await _sut.SetRequireMfaForMembersAsync(_tenantId, _callerId, true, CancellationToken.None);

        Assert.True(tenant.RequireMfaForMembers);
        Assert.Equal(Now, tenant.UpdatedAt);
        _authorization.Verify(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ManageSettings, It.IsAny<CancellationToken>()), Times.Once);
        _tenants.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetRequireMfaForMembersAsync_WhenCallerLacksPermission_PropagatesForbiddenWithoutTouchingTheTenant()
    {
        _authorization
            .Setup(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ManageSettings, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("nope"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.SetRequireMfaForMembersAsync(_tenantId, _callerId, true, CancellationToken.None));

        _tenants.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
