using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orbita.Application.Billing;
using Orbita.Application.Channels;
using Orbita.Domain.Channels;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class WebhookIngestionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IChannelAccountRepository> _accounts = new();
    private readonly Mock<IWebhookSignatureVerifier> _signatureVerifier = new();
    private readonly Mock<IWebhookDeduplicator> _deduplicator = new();
    private readonly Mock<IInboundWebhookQueue> _queue = new();
    private readonly WebhookIngestionService _sut;

    public WebhookIngestionServiceTests()
    {
        _deduplicator.Setup(d => d.TryMarkSeenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _sut = new WebhookIngestionService(
            _accounts.Object,
            _signatureVerifier.Object,
            _deduplicator.Object,
            _queue.Object,
            new FixedTimeProvider(Now),
            NullLogger<WebhookIngestionService>.Instance);
    }

    [Fact]
    public async Task IngestAsync_WithInvalidSignature_ThrowsAndNeverEnqueues()
    {
        _signatureVerifier
            .Setup(v => v.Verify(It.IsAny<byte[]>(), It.IsAny<string?>()))
            .Throws<InvalidWebhookSignatureException>();

        await Assert.ThrowsAsync<InvalidWebhookSignatureException>(
            () => _sut.IngestAsync(ChannelKind.WhatsApp, null, "{}"u8.ToArray(), "sha256=bad", CancellationToken.None));

        _accounts.Verify(a => a.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<InboundWebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IngestAsync_ByAccountRoute_WhenPayloadPhoneMismatch_IsUnroutable()
    {
        var account = CreateConnectedAccount("phone-1");
        _accounts.Setup(a => a.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var payload = WhatsAppPayload("phone-other");

        var result = await _sut.IngestAsync(ChannelKind.WhatsApp, account.Id, payload, "sha256=ok", CancellationToken.None);

        Assert.Equal(WebhookIngestionResult.Unroutable, result);
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<InboundWebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IngestAsync_ByGlobalRoute_ResolvesAccountFromPayload()
    {
        var account = CreateConnectedAccount("phone-1");
        _accounts
            .Setup(a => a.FindByKindAndExternalIdAsync(ChannelKind.WhatsApp, "phone-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        var payload = WhatsAppPayload("phone-1");

        var result = await _sut.IngestAsync(ChannelKind.WhatsApp, null, payload, "sha256=ok", CancellationToken.None);

        Assert.Equal(WebhookIngestionResult.Accepted, result);
        _queue.Verify(
            q => q.EnqueueAsync(It.Is<InboundWebhookEvent>(e => e.TenantId == account.TenantId && e.ChannelAccountId == account.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IngestAsync_DuplicateHash_IsNotEnqueuedTwice()
    {
        var account = CreateConnectedAccount("phone-1");
        _accounts.Setup(a => a.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        _deduplicator.Setup(d => d.TryMarkSeenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var payload = WhatsAppPayload("phone-1");

        var result = await _sut.IngestAsync(ChannelKind.WhatsApp, account.Id, payload, "sha256=ok", CancellationToken.None);

        Assert.Equal(WebhookIngestionResult.Duplicate, result);
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<InboundWebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IngestAsync_UnknownAccount_IsUnroutableWithoutThrowing()
    {
        _accounts
            .Setup(a => a.FindByKindAndExternalIdAsync(It.IsAny<ChannelKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelAccount?)null);
        var payload = WhatsAppPayload("phone-unknown");

        var result = await _sut.IngestAsync(ChannelKind.WhatsApp, null, payload, "sha256=ok", CancellationToken.None);

        Assert.Equal(WebhookIngestionResult.Unroutable, result);
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<InboundWebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ChannelAccount CreateConnectedAccount(string phoneNumberId)
    {
        var account = ChannelAccount.ConnectWhatsApp(Guid.NewGuid(), phoneNumberId, "waba-1", "Acme", null, "local://a", null, Now);
        account.MarkConnected(Now);
        return account;
    }

    private static byte[] WhatsAppPayload(string phoneNumberId)
        => System.Text.Encoding.UTF8.GetBytes(
            "{\"object\":\"whatsapp_business_account\",\"entry\":[{\"id\":\"waba-1\",\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\""
            + phoneNumberId + "\"}}}]}]}");
}
