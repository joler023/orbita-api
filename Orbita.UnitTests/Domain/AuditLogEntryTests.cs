using Orbita.Domain.Audit;

namespace Orbita.UnitTests.Domain;

public sealed class AuditLogEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordForUser_SetsUserActorTypeAndActorId()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var entry = AuditLogEntry.RecordForUser(
            tenantId, actorId, "membership.role_changed", "Membership", entityId, """{"from":"agent","to":"admin"}""", "203.0.113.1", "curl/8.0", Now);

        Assert.Equal(0, entry.Id);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal(actorId, entry.ActorId);
        Assert.Equal(AuditActorType.User, entry.ActorType);
        Assert.Equal("membership.role_changed", entry.Action);
        Assert.Equal("Membership", entry.EntityType);
        Assert.Equal(entityId, entry.EntityId);
        Assert.Equal("203.0.113.1", entry.IpAddress);
        Assert.Equal("curl/8.0", entry.UserAgent);
        Assert.Equal(Now, entry.CreatedAt);
    }

    [Fact]
    public void RecordForSystem_LeavesActorIdNull()
    {
        var entry = AuditLogEntry.RecordForSystem(Guid.NewGuid(), AuditActorType.System, "subscription.past_due", "Subscription", Guid.NewGuid(), null, Now);

        Assert.Null(entry.ActorId);
        Assert.Equal(AuditActorType.System, entry.ActorType);
        Assert.Null(entry.IpAddress);
        Assert.Null(entry.UserAgent);
    }

    [Fact]
    public void RecordForSystem_WithUserActorType_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => AuditLogEntry.RecordForSystem(Guid.NewGuid(), AuditActorType.User, "action", "Entity", null, null, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RecordForUser_WithEmptyAction_Throws(string action)
    {
        Assert.Throws<ArgumentException>(
            () => AuditLogEntry.RecordForUser(Guid.NewGuid(), Guid.NewGuid(), action, "Entity", null, null, null, null, Now));
    }

    [Fact]
    public void RecordForUser_WithActionTooLong_Throws()
    {
        var tooLong = new string('a', AuditLogEntry.ActionMaxLength + 1);

        Assert.Throws<ArgumentException>(
            () => AuditLogEntry.RecordForUser(Guid.NewGuid(), Guid.NewGuid(), tooLong, "Entity", null, null, null, null, Now));
    }
}
