using Orbita.Domain.Identity;

namespace Orbita.UnitTests.Domain;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    [Fact]
    public void IssueNewFamily_CreatesAnActiveTokenWithItsOwnFamily()
    {
        var userId = Guid.NewGuid();

        var token = RefreshToken.IssueNewFamily(userId, "hash", Now, Lifetime);

        Assert.Equal(userId, token.UserId);
        Assert.Equal("hash", token.TokenHash);
        Assert.Equal(Now + Lifetime, token.ExpiresAt);
        Assert.Null(token.RevokedAt);
        Assert.Null(token.ReplacedByTokenId);
        Assert.True(token.IsActive(Now));
    }

    [Fact]
    public void IssueInFamily_ReusesTheGivenFamilyId()
    {
        var first = RefreshToken.IssueNewFamily(Guid.NewGuid(), "hash-1", Now, Lifetime);

        var second = RefreshToken.IssueInFamily(first.UserId, "hash-2", first.FamilyId, Now, Lifetime);

        Assert.Equal(first.FamilyId, second.FamilyId);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void IsActive_AfterExpiry_ReturnsFalse()
    {
        var token = RefreshToken.IssueNewFamily(Guid.NewGuid(), "hash", Now, Lifetime);

        Assert.False(token.IsActive(Now + Lifetime));
    }

    [Fact]
    public void RevokeReplacedBy_SetsRevokedAtAndReplacedByTokenId()
    {
        var token = RefreshToken.IssueNewFamily(Guid.NewGuid(), "hash", Now, Lifetime);
        var replacementId = Guid.NewGuid();
        var revokedAt = Now.AddMinutes(5);

        token.RevokeReplacedBy(replacementId, revokedAt);

        Assert.Equal(revokedAt, token.RevokedAt);
        Assert.Equal(replacementId, token.ReplacedByTokenId);
        Assert.False(token.IsActive(revokedAt));
    }

    [Fact]
    public void Revoke_SetsRevokedAtWithoutAReplacement()
    {
        var token = RefreshToken.IssueNewFamily(Guid.NewGuid(), "hash", Now, Lifetime);
        var revokedAt = Now.AddMinutes(5);

        token.Revoke(revokedAt);

        Assert.Equal(revokedAt, token.RevokedAt);
        Assert.Null(token.ReplacedByTokenId);
    }
}
