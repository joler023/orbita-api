using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orbita.Application.Audit;
using Orbita.Application.Channels;
using Orbita.Application.Identity;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class WhatsAppChannelServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IChannelAccountRepository> _accounts = new();
    private readonly Mock<IMetaAuthClient> _metaAuth = new();
    private readonly Mock<IWhatsAppCloudApiClient> _whatsApp = new();
    private readonly Mock<IChannelCredentialStore> _credentials = new();
    private readonly Mock<IChannelWebhookSettings> _settings = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly WhatsAppChannelService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();
    private readonly ConnectWhatsAppRequest _request = new("oauth-code", "waba-1", "phone-1");

    public WhatsAppChannelServiceTests()
    {
        _settings.SetupGet(s => s.PublicBaseUrl).Returns("https://api.test");
        _settings.SetupGet(s => s.GlobalVerifyToken).Returns("global");
        _metaAuth.Setup(m => m.ExchangeCodeAsync("oauth-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetaAccessToken("access-token", Now.AddDays(60)));
        _whatsApp.Setup(w => w.GetPhoneNumberAsync("access-token", "phone-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppPhoneNumberInfo("+57 300 111 2233", "Acme Store"));
        _credentials.Setup(c => c.StoreAsync("access-token", It.IsAny<CancellationToken>())).ReturnsAsync("local://new");

        _sut = new WhatsAppChannelService(
            _accounts.Object,
            _metaAuth.Object,
            _whatsApp.Object,
            _credentials.Object,
            _settings.Object,
            _authorization.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now),
            NullLogger<WhatsAppChannelService>.Instance);
    }

    [Fact]
    public async Task ConnectAsync_ExchangesCodeStoresTokenAndSavesAPendingAccount()
    {
        ChannelAccount? added = null;
        _accounts.Setup(r => r.AddAsync(It.IsAny<ChannelAccount>(), It.IsAny<CancellationToken>()))
            .Callback<ChannelAccount, CancellationToken>((a, _) => added = a)
            .Returns(Task.CompletedTask);

        var dto = await _sut.ConnectAsync(_tenantId, _callerId, _request, CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(ChannelStatus.PendingVerification, dto.Status);
        Assert.Equal("Acme Store", dto.DisplayName);
        Assert.Equal("+573001112233", dto.PhoneE164);
        Assert.Equal("phone-1", dto.ExternalId);
        Assert.Equal(Now.AddDays(60), dto.TokenExpiresAt);
        Assert.False(dto.ExpiresSoon);
        Assert.Equal("local://new", added!.CredentialsRef);
        Assert.Equal("waba-1", added.WabaId);
        _authorization.Verify(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ManageChannels, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditLogger.Verify(
            a => a.RecordAsync(_tenantId, _callerId, "channel.connected", nameof(ChannelAccount), added.Id, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _whatsApp.Verify(
            w => w.SubscribeWebhookAsync("access-token", "waba-1", $"https://api.test/api/webhooks/whatsapp/{added.Id:D}", added.WebhookSecret, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConnectAsync_WhenMetaDoesNotReportExpiryOnTheToken_AsksForItSeparately()
    {
        _metaAuth.Setup(m => m.ExchangeCodeAsync("oauth-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetaAccessToken("access-token", null));
        _metaAuth.Setup(m => m.GetTokenExpiryAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Now.AddDays(3));

        var dto = await _sut.ConnectAsync(_tenantId, _callerId, _request, CancellationToken.None);

        Assert.Equal(Now.AddDays(3), dto.TokenExpiresAt);
        Assert.True(dto.ExpiresSoon);
    }

    [Fact]
    public async Task ConnectAsync_WhenPhoneNumberBelongsToAnotherTenant_ThrowsWithoutTalkingToMeta()
    {
        var foreign = ChannelAccount.ConnectWhatsApp(Guid.NewGuid(), "phone-1", "waba-x", "Other", null, "local://x", null, Now);
        _accounts.Setup(r => r.FindByKindAndExternalIdAsync(ChannelKind.WhatsApp, "phone-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(foreign);

        await Assert.ThrowsAsync<ChannelAlreadyConnectedException>(
            () => _sut.ConnectAsync(_tenantId, _callerId, _request, CancellationToken.None));

        _metaAuth.Verify(m => m.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConnectAsync_ReconnectingTheSameTenantsNumber_RotatesCredentialsAndDeletesTheOldOnes()
    {
        var existing = ChannelAccount.ConnectWhatsApp(_tenantId, "phone-1", "waba-1", "Old Name", null, "local://old", null, Now);
        existing.MarkConnected(Now);
        _accounts.Setup(r => r.FindByKindAndExternalIdAsync(ChannelKind.WhatsApp, "phone-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var dto = await _sut.ConnectAsync(_tenantId, _callerId, _request, CancellationToken.None);

        Assert.Equal(existing.Id, dto.Id);
        Assert.Equal("local://new", existing.CredentialsRef);
        Assert.Equal("Acme Store", existing.DisplayName);
        Assert.Equal(ChannelStatus.PendingVerification, existing.Status);
        _credentials.Verify(c => c.DeleteAsync("local://old", It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Verify(r => r.AddAsync(It.IsAny<ChannelAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConnectAsync_WhenWebhookSubscriptionFails_StillReturnsThePendingAccount()
    {
        _whatsApp.Setup(w => w.SubscribeWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MetaApiException("callback verification failed", 2200));

        var dto = await _sut.ConnectAsync(_tenantId, _callerId, _request, CancellationToken.None);

        Assert.Equal(ChannelStatus.PendingVerification, dto.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectAsync_WhenTheCodeExchangeFails_ThrowsConnectionFailedAndStoresNothing()
    {
        _metaAuth.Setup(m => m.ExchangeCodeAsync("oauth-code", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MetaApiException("code expired", 100));

        await Assert.ThrowsAsync<ChannelConnectionFailedException>(
            () => _sut.ConnectAsync(_tenantId, _callerId, _request, CancellationToken.None));

        _credentials.Verify(c => c.StoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConnectAsync_WithoutAPublicBaseUrl_SubscribesWithoutACallbackOverride()
    {
        _settings.SetupGet(s => s.PublicBaseUrl).Returns(string.Empty);

        await _sut.ConnectAsync(_tenantId, _callerId, _request, CancellationToken.None);

        _whatsApp.Verify(w => w.SubscribeWebhookAsync("access-token", "waba-1", null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectAsync_WhenCallerLacksPermission_PropagatesForbiddenBeforeAnySideEffect()
    {
        _authorization
            .Setup(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ManageChannels, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("nope"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.ConnectAsync(_tenantId, _callerId, _request, CancellationToken.None));

        _metaAuth.Verify(m => m.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _accounts.Verify(r => r.FindByKindAndExternalIdAsync(It.IsAny<ChannelKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_RequiresViewChannelsAndMapsExpiryWarning()
    {
        var soon = ChannelAccount.ConnectWhatsApp(_tenantId, "phone-1", "waba-1", "A", null, "local://a", Now.AddDays(2), Now);
        var later = ChannelAccount.ConnectWhatsApp(_tenantId, "phone-2", "waba-1", "B", null, "local://b", Now.AddDays(30), Now);
        _accounts.Setup(r => r.ListByTenantAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChannelAccount>)[soon, later]);

        var result = await _sut.ListAsync(_tenantId, _callerId, CancellationToken.None);

        Assert.Collection(result, a => Assert.True(a.ExpiresSoon), b => Assert.False(b.ExpiresSoon));
        _authorization.Verify(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ViewChannels, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ForAnAccountOfAnotherTenant_ThrowsNotFound()
    {
        var foreign = ChannelAccount.ConnectWhatsApp(Guid.NewGuid(), "phone-1", "waba-1", "A", null, "local://a", null, Now);
        _accounts.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        await Assert.ThrowsAsync<ChannelAccountNotFoundException>(
            () => _sut.GetAsync(_tenantId, _callerId, foreign.Id, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyAsync_WhenTheSubscriptionFailsAgain_ThrowsConnectionFailed()
    {
        var account = ChannelAccount.ConnectWhatsApp(_tenantId, "phone-1", "waba-1", "A", null, "local://a", null, Now);
        _accounts.Setup(r => r.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        _credentials.Setup(c => c.GetAsync("local://a", It.IsAny<CancellationToken>())).ReturnsAsync("stored-token");
        _whatsApp.Setup(w => w.SubscribeWebhookAsync("stored-token", "waba-1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MetaApiException("still failing"));

        await Assert.ThrowsAsync<ChannelConnectionFailedException>(
            () => _sut.VerifyAsync(_tenantId, _callerId, account.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DisconnectAsync_MarksDisconnectedSavesThenDeletesTheCredential()
    {
        var account = ChannelAccount.ConnectWhatsApp(_tenantId, "phone-1", "waba-1", "A", null, "local://a", null, Now);
        account.MarkConnected(Now);
        _accounts.Setup(r => r.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var order = new List<string>();
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save")).Returns(Task.CompletedTask);
        _credentials.Setup(c => c.DeleteAsync("local://a", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("delete")).Returns(Task.CompletedTask);

        await _sut.DisconnectAsync(_tenantId, _callerId, account.Id, CancellationToken.None);

        Assert.Equal(ChannelStatus.Disconnected, account.Status);
        Assert.Equal(["save", "delete"], order);
        _auditLogger.Verify(
            a => a.RecordAsync(_tenantId, _callerId, "channel.disconnected", nameof(ChannelAccount), account.Id, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
