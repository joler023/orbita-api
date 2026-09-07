using Moq;
using Orbita.Application.Audit;
using Orbita.Application.Common;
using Orbita.Domain.Audit;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class AuditLoggerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAuditLogRepository> _repository = new();
    private readonly Mock<IRequestContext> _requestContext = new();
    private readonly AuditLogger _sut;

    public AuditLoggerTests()
    {
        _requestContext.Setup(r => r.IpAddress).Returns("203.0.113.1");
        _requestContext.Setup(r => r.UserAgent).Returns("curl/8.0");
        _sut = new AuditLogger(_repository.Object, _requestContext.Object, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task RecordAsync_StagesAUserAttributedEntryWithTheRequestContext()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        await _sut.RecordAsync(tenantId, actorId, "membership.role_changed", "Membership", entityId, new { from = "Agent", to = "Admin" }, CancellationToken.None);

        _repository.Verify(
            r => r.AddAsync(
                It.Is<AuditLogEntry>(e =>
                    e.TenantId == tenantId
                    && e.ActorId == actorId
                    && e.ActorType == AuditActorType.User
                    && e.Action == "membership.role_changed"
                    && e.EntityType == "Membership"
                    && e.EntityId == entityId
                    && e.IpAddress == "203.0.113.1"
                    && e.UserAgent == "curl/8.0"
                    && e.DiffJson != null && e.DiffJson.Contains("Agent") && e.DiffJson.Contains("Admin")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordAsync_WithNoDiff_LeavesDiffJsonNull()
    {
        await _sut.RecordAsync(Guid.NewGuid(), Guid.NewGuid(), "auth.login", "User", null, null, CancellationToken.None);

        _repository.Verify(r => r.AddAsync(It.Is<AuditLogEntry>(e => e.DiffJson == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordSystemActionAsync_LeavesActorIdAndRequestContextNull()
    {
        var tenantId = Guid.NewGuid();

        await _sut.RecordSystemActionAsync(tenantId, AuditActorType.System, "subscription.past_due", "Subscription", null, null, CancellationToken.None);

        _repository.Verify(
            r => r.AddAsync(
                It.Is<AuditLogEntry>(e => e.TenantId == tenantId && e.ActorId == null && e.ActorType == AuditActorType.System && e.IpAddress == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
