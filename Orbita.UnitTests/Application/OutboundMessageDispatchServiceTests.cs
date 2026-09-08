using Moq;
using Orbita.Application.Channels;
using Orbita.Application.Inbox;
using Orbita.Application.Outbox;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Inbox;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class OutboundMessageDispatchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IChannelAccountRepository> _channelAccounts = new();
    private readonly Mock<IChannelAdapter> _adapter = new();
    private readonly Mock<IOutboundMessageRateLimiter> _rateLimiter = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly OutboundMessageDispatchService _sut;

    private readonly Message _message;
    private readonly Conversation _conversation;
    private readonly Contact _contact;
    private readonly ChannelAccount _account;
    private readonly OutboundMessageJob _job;

    public OutboundMessageDispatchServiceTests()
    {
        _contact = Contact.Create(TenantId, "Ana", Now, phone: "573001234567");
        _conversation = Conversation.Open(TenantId, _contact.Id, Guid.NewGuid(), Now);
        _message = Message.OutboundText(TenantId, _conversation.Id, "hola", Guid.NewGuid(), MessageCategory.Service, Now);
        _account = ChannelAccount.ConnectWhatsApp(TenantId, "phone-1", "waba-1", "Acme", null, "local://a", null, Now);
        _account.MarkConnected(Now);
        _job = OutboundMessageJob.Create(TenantId, _message.Id, _message.CreatedAt, _account.Id, Now);

        _adapter.SetupGet(a => a.Kind).Returns(ChannelKind.WhatsApp);
        _messages.Setup(m => m.GetByIdAsync(_message.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_message);
        _conversations.Setup(c => c.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _contacts.Setup(c => c.GetByIdAsync(_contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _channelAccounts.Setup(a => a.GetByIdAsync(_account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_account);

        _sut = new OutboundMessageDispatchService(
            _messages.Object,
            _conversations.Object,
            _contacts.Object,
            _channelAccounts.Object,
            new[] { _adapter.Object },
            _rateLimiter.Object,
            _outboxWriter.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task DispatchAsync_Success_MarksSentWithExternalId()
    {
        _adapter
            .Setup(a => a.SendTextAsync(_account, "573001234567", "hola", It.IsAny<CancellationToken>()))
            .ReturnsAsync("wamid.123");

        await _sut.DispatchAsync(_job, CancellationToken.None);

        Assert.Equal(MessageStatus.Sent, _message.Status);
        Assert.Equal("wamid.123", _message.ExternalId);
        Assert.Equal(OutboundJobStatus.Sent, _job.Status);
        _outboxWriter.Verify(w => w.StageAsync(TenantId, nameof(Message), _message.Id, "message.sent", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_TransientError_SchedulesRetry()
    {
        _adapter
            .Setup(a => a.SendTextAsync(_account, "573001234567", "hola", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelSendException("130429", isTransient: true, "rate limited"));

        await _sut.DispatchAsync(_job, CancellationToken.None);

        Assert.Equal(OutboundJobStatus.Pending, _job.Status);
        Assert.Equal(1, _job.Attempts);
        Assert.True(_job.NextAttemptAt > Now);
        Assert.Equal(MessageStatus.Queued, _message.Status);
    }

    [Fact]
    public async Task DispatchAsync_PermanentError_MarksFailedWithCode()
    {
        _adapter
            .Setup(a => a.SendTextAsync(_account, "573001234567", "hola", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelSendException("131047", isTransient: false, "window closed"));

        await _sut.DispatchAsync(_job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, _message.Status);
        Assert.Equal("131047", _message.ErrorCode);
        Assert.Equal(OutboundJobStatus.Failed, _job.Status);
        _outboxWriter.Verify(w => w.StageAsync(TenantId, nameof(Message), _message.Id, "message.failed", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_TokenExpiredError_MarksAccountExpired()
    {
        _adapter
            .Setup(a => a.SendTextAsync(_account, "573001234567", "hola", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelSendException("190", isTransient: false, "token expired"));

        await _sut.DispatchAsync(_job, CancellationToken.None);

        Assert.Equal(ChannelStatus.TokenExpired, _account.Status);
    }

    [Fact]
    public async Task DispatchAsync_AcquiresRateLimiterForTheAccount()
    {
        _adapter.Setup(a => a.SendTextAsync(_account, "573001234567", "hola", It.IsAny<CancellationToken>())).ReturnsAsync("wamid.1");

        await _sut.DispatchAsync(_job, CancellationToken.None);

        _rateLimiter.Verify(r => r.AcquireAsync(_account.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
