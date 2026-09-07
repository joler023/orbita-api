using Moq;
using Orbita.Application.Audit;
using Orbita.Application.Identity;
using Orbita.Domain.Audit;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.UnitTests.Application;

public sealed class AuditLogQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAuditLogRepository> _repository = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly AuditLogQueryService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    public AuditLogQueryServiceTests()
    {
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<AuditLogEntry>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<AuditLogEntry>>> query, CancellationToken ct) => query(ct));

        _sut = new AuditLogQueryService(_repository.Object, _authorization.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task QueryAsync_ReturnsMappedEntries()
    {
        var entry = AuditLogEntry.RecordForUser(_tenantId, _callerId, "membership.removed", "Membership", Guid.NewGuid(), null, null, null, Now);
        var query = new AuditLogQuery();
        _repository.Setup(r => r.QueryAsync(_tenantId, query, It.IsAny<CancellationToken>())).ReturnsAsync([entry]);

        var result = await _sut.QueryAsync(_tenantId, _callerId, query, CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal(entry.Action, dto.Action);
        Assert.Equal(entry.EntityType, dto.EntityType);
        _authorization.Verify(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ViewAuditLog, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryAsync_WhenCallerLacksPermission_PropagatesForbiddenWithoutQuerying()
    {
        _authorization
            .Setup(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ViewAuditLog, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("nope"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.QueryAsync(_tenantId, _callerId, new AuditLogQuery(), CancellationToken.None));

        _repository.Verify(r => r.QueryAsync(It.IsAny<Guid>(), It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
