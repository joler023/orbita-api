using Orbita.Domain.Identity;

namespace Orbita.UnitTests.Domain;

public sealed class TwoFactorBackupCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_CreatesAnUnusedCode()
    {
        var userId = Guid.NewGuid();

        var code = TwoFactorBackupCode.Issue(userId, "hash", Now);

        Assert.Equal(userId, code.UserId);
        Assert.Equal("hash", code.CodeHash);
        Assert.False(code.IsUsed);
        Assert.Null(code.UsedAt);
    }

    [Fact]
    public void MarkUsed_SetsUsedAt()
    {
        var code = TwoFactorBackupCode.Issue(Guid.NewGuid(), "hash", Now);

        code.MarkUsed(Now.AddMinutes(5));

        Assert.True(code.IsUsed);
        Assert.Equal(Now.AddMinutes(5), code.UsedAt);
    }
}
