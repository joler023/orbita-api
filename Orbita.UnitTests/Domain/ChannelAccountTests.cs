using Orbita.Domain.Channels;

namespace Orbita.UnitTests.Domain;

public sealed class ChannelAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConnectWhatsApp_StartsPendingVerificationWithARandomWebhookSecret()
    {
        var account = CreateAccount();

        Assert.Equal(ChannelKind.WhatsApp, account.Kind);
        Assert.Equal(ChannelStatus.PendingVerification, account.Status);
        Assert.Null(account.ConnectedAt);
        Assert.Equal(64, account.WebhookSecret.Length);
        Assert.NotEqual(account.WebhookSecret, CreateAccount().WebhookSecret);
    }

    [Fact]
    public void ConnectWhatsApp_WithEmptyTenant_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChannelAccount.ConnectWhatsApp(
            Guid.Empty, "123", "waba", "Acme", "+573001112233", "local://x", null, Now));
    }

    [Fact]
    public void MarkConnected_SetsConnectedAtOnlyOnce()
    {
        var account = CreateAccount();

        account.MarkConnected(Now);
        account.MarkConnected(Now.AddHours(1));

        Assert.Equal(ChannelStatus.Connected, account.Status);
        Assert.Equal(Now, account.ConnectedAt);
    }

    [Fact]
    public void VerifyWebhookToken_AcceptsOnlyTheExactSecret()
    {
        var account = CreateAccount();

        Assert.True(account.VerifyWebhookToken(account.WebhookSecret));
        Assert.False(account.VerifyWebhookToken(account.WebhookSecret + "x"));
        Assert.False(account.VerifyWebhookToken(null));
        Assert.False(account.VerifyWebhookToken(string.Empty));
    }

    [Fact]
    public void TokenExpiresSoon_IsTrueWithinSevenDaysOfExpiry()
    {
        var account = CreateAccount(tokenExpiresAt: Now.AddDays(5));

        Assert.True(account.TokenExpiresSoon(Now));
        Assert.False(account.TokenExpiresSoon(Now.AddDays(-10)));
        Assert.False(account.IsTokenExpired(Now));
        Assert.True(account.IsTokenExpired(Now.AddDays(6)));
    }

    [Fact]
    public void TokenExpiresSoon_WithoutAnExpiry_IsFalse()
    {
        var account = CreateAccount(tokenExpiresAt: null);

        Assert.False(account.TokenExpiresSoon(Now));
        Assert.False(account.IsTokenExpired(Now));
    }

    [Fact]
    public void MarkTokenExpired_DoesNotResurrectADisconnectedAccount()
    {
        var account = CreateAccount();
        account.Disconnect();

        account.MarkTokenExpired();

        Assert.Equal(ChannelStatus.Disconnected, account.Status);
    }

    [Fact]
    public void Disconnect_KeepsIdentityAndCredentialsRefForTheServiceToClean()
    {
        var account = CreateAccount();
        account.MarkConnected(Now);

        account.Disconnect();

        Assert.Equal(ChannelStatus.Disconnected, account.Status);
        Assert.Equal("local://x", account.CredentialsRef);
        Assert.Equal("123", account.ExternalId);
    }

    [Fact]
    public void RotateCredentials_ReplacesTheReferenceAndGoesBackToVerification()
    {
        var account = CreateAccount();
        account.MarkConnected(Now);

        account.RotateCredentials("local://y", Now.AddDays(60), "Acme Renamed");

        Assert.Equal("local://y", account.CredentialsRef);
        Assert.Equal(Now.AddDays(60), account.TokenExpiresAt);
        Assert.Equal("Acme Renamed", account.DisplayName);
        Assert.Equal(ChannelStatus.PendingVerification, account.Status);
    }

    private static ChannelAccount CreateAccount(DateTimeOffset? tokenExpiresAt = null)
        => ChannelAccount.ConnectWhatsApp(
            Guid.NewGuid(), "123", "waba-1", "Acme", "+573001112233", "local://x", tokenExpiresAt, Now);
}
