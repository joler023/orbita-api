using Moq;
using Orbita.Application.Audit;
using Orbita.Application.Channels;
using Orbita.Domain.Audit;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class ChannelWebhookVerificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IChannelAccountRepository> _accounts = new();
    private readonly Mock<IChannelWebhookSettings> _settings = new();
    private readonly Mock<ITenantContextSetter> _tenantContext = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly ChannelWebhookVerificationService _sut;

    public ChannelWebhookVerificationServiceTests()
    {
        _settings.SetupGet(s => s.GlobalVerifyToken).Returns("global-token");
        _sut = new ChannelWebhookVerificationService(
            _accounts.Object,
            _settings.Object,
            _tenantContext.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task VerifyWhatsAppAsync_WithTheAccountsOwnToken_MarksItConnectedAndEchoesTheChallenge()
    {
        var account = CreatePendingAccount();
        _accounts.Setup(r => r.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var result = await _sut.VerifyWhatsAppAsync(account.Id, "subscribe", account.WebhookSecret, "challenge-123", CancellationToken.None);

        Assert.Equal("challenge-123", result);
        Assert.Equal(ChannelStatus.Connected, account.Status);
        Assert.Equal(Now, account.ConnectedAt);
        _tenantContext.Verify(t => t.SetTenant(account.TenantId), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditLogger.Verify(
            a => a.RecordSystemActionAsync(account.TenantId, AuditActorType.System, "channel.webhook_verified", nameof(ChannelAccount), account.Id, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyWhatsAppAsync_WithAWrongAccountToken_Throws()
    {
        var account = CreatePendingAccount();
        _accounts.Setup(r => r.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        await Assert.ThrowsAsync<WebhookVerificationFailedException>(
            () => _sut.VerifyWhatsAppAsync(account.Id, "subscribe", "not-it", "challenge", CancellationToken.None));

        Assert.Equal(ChannelStatus.PendingVerification, account.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VerifyWhatsAppAsync_ForAnUnknownAccount_Throws()
    {
        _accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ChannelAccount?)null);

        await Assert.ThrowsAsync<WebhookVerificationFailedException>(
            () => _sut.VerifyWhatsAppAsync(Guid.NewGuid(), "subscribe", "anything", "challenge", CancellationToken.None));
    }

    [Fact]
    public async Task VerifyWhatsAppAsync_OnTheAppLevelRoute_ComparesAgainstTheConfiguredToken()
    {
        var result = await _sut.VerifyWhatsAppAsync(null, "subscribe", "global-token", "challenge-9", CancellationToken.None);

        Assert.Equal("challenge-9", result);
        await Assert.ThrowsAsync<WebhookVerificationFailedException>(
            () => _sut.VerifyWhatsAppAsync(null, "subscribe", "wrong", "challenge-9", CancellationToken.None));
        _accounts.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VerifyWhatsAppAsync_WithAnEmptyConfiguredToken_NeverVerifiesTheAppLevelRoute()
    {
        _settings.SetupGet(s => s.GlobalVerifyToken).Returns(string.Empty);

        await Assert.ThrowsAsync<WebhookVerificationFailedException>(
            () => _sut.VerifyWhatsAppAsync(null, "subscribe", string.Empty, "challenge", CancellationToken.None));
    }

    [Theory]
    [InlineData("unsubscribe", "challenge")]
    [InlineData(null, "challenge")]
    [InlineData("subscribe", null)]
    [InlineData("subscribe", "")]
    public async Task VerifyWhatsAppAsync_WithAWrongModeOrMissingChallenge_Throws(string? mode, string? challenge)
    {
        await Assert.ThrowsAsync<WebhookVerificationFailedException>(
            () => _sut.VerifyWhatsAppAsync(null, mode, "global-token", challenge, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyWhatsAppAsync_ForAnAlreadyConnectedAccount_EchoesWithoutSavingAgain()
    {
        var account = CreatePendingAccount();
        account.MarkConnected(Now.AddDays(-1));
        _accounts.Setup(r => r.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var result = await _sut.VerifyWhatsAppAsync(account.Id, "subscribe", account.WebhookSecret, "again", CancellationToken.None);

        Assert.Equal("again", result);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ChannelAccount CreatePendingAccount()
        => ChannelAccount.ConnectWhatsApp(Guid.NewGuid(), "phone-1", "waba-1", "Acme", null, "local://a", null, Now);
}
