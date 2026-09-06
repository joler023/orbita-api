using Orbita.Domain.Identity;

namespace Orbita.UnitTests.Domain;

public sealed class InvitationTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    [Fact]
    public void Issue_CreatesAValidToken()
    {
        var membershipId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var token = InvitationToken.Issue(membershipId, tenantId, "hash", Now, Lifetime);

        Assert.Equal(membershipId, token.MembershipId);
        Assert.Equal(tenantId, token.TenantId);
        Assert.Equal("hash", token.TokenHash);
        Assert.Equal(Now + Lifetime, token.ExpiresAt);
        Assert.Null(token.UsedAt);
        Assert.True(token.IsValid(Now));
    }

    [Fact]
    public void IsValid_AfterExpiry_ReturnsFalse()
    {
        var token = InvitationToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "hash", Now, Lifetime);

        Assert.False(token.IsValid(Now + Lifetime));
    }

    [Fact]
    public void MarkUsed_MakesTheTokenInvalid()
    {
        var token = InvitationToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "hash", Now, Lifetime);

        token.MarkUsed(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(5), token.UsedAt);
        Assert.False(token.IsValid(Now.AddMinutes(5)));
    }
}
