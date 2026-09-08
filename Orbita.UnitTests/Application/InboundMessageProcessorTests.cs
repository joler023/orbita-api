using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orbita.Application.Channels;
using Orbita.Application.Inbox;
using Orbita.Application.Media;
using Orbita.Application.Outbox;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Inbox;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class InboundMessageProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly ChannelAccount FakeAccount = ChannelAccount.ConnectWhatsApp(TenantId, "phone-1", "waba-1", "Acme", null, "local://a", null, Now);
    private static readonly Guid ChannelAccountId = FakeAccount.Id;

    private readonly Mock<IChannelAdapter> _adapter = new();
    private readonly Mock<IChannelAccountRepository> _channelAccounts = new();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<IMediaStorage> _mediaStorage = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly InboundMessageProcessor _sut;

    public InboundMessageProcessorTests()
    {
        _adapter.SetupGet(a => a.Kind).Returns(ChannelKind.WhatsApp);
        _channelAccounts
            .Setup(a => a.GetByIdAsync(ChannelAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeAccount);
        _messages.Setup(m => m.FindByExternalIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Message?)null);
        _conversations
            .Setup(c => c.FindOpenByContactAndAccountAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _contacts
            .Setup(c => c.FindByPhoneAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Contact?)null);

        _sut = new InboundMessageProcessor(
            new[] { _adapter.Object },
            _channelAccounts.Object,
            _contacts.Object,
            _conversations.Object,
            _messages.Object,
            _mediaStorage.Object,
            _outboxWriter.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now),
            NullLogger<InboundMessageProcessor>.Instance);
    }

    [Fact]
    public async Task ProcessAsync_NewSender_CreatesContactConversationAndMessage()
    {
        SetupItems(new InboundMessage("phone-1", "wamid.1", "573001234567", "Ana", ChannelKind.WhatsApp, "hola", null, null, null, Now));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        _contacts.Verify(c => c.AddAsync(It.Is<Contact>(x => x.Phone == "573001234567" && x.DisplayName == "Ana"), It.IsAny<CancellationToken>()), Times.Once);
        _conversations.Verify(c => c.AddAsync(It.IsAny<Conversation>(), It.IsAny<CancellationToken>()), Times.Once);
        _messages.Verify(m => m.AddAsync(It.Is<Message>(x => x.ExternalId == "wamid.1" && x.Body == "hola"), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ExistingContactAndOpenConversation_AppendsAndRefreshesWindow()
    {
        var contact = Contact.Create(TenantId, "Ana", Now, phone: "573001234567");
        var conversation = Conversation.Open(TenantId, contact.Id, ChannelAccountId, Now.AddDays(-1));
        _contacts.Setup(c => c.FindByPhoneAsync(TenantId, "573001234567", It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        _conversations
            .Setup(c => c.FindOpenByContactAndAccountAsync(contact.Id, ChannelAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        SetupItems(new InboundMessage("phone-1", "wamid.2", "573001234567", "Ana", ChannelKind.WhatsApp, "de nuevo", null, null, null, Now));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        _contacts.Verify(c => c.AddAsync(It.IsAny<Contact>(), It.IsAny<CancellationToken>()), Times.Never);
        _conversations.Verify(c => c.AddAsync(It.IsAny<Conversation>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(Now, conversation.LastMessageAt);
        Assert.Equal(Now, contact.LastSeenAt);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateExternalId_DoesNotInsertOrSave()
    {
        _messages.Setup(m => m.FindByExternalIdAsync("wamid.dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Message.Inbound(TenantId, Guid.NewGuid(), "wamid.dup", "hola", null, null, Now));
        SetupItems(new InboundMessage("phone-1", "wamid.dup", "573001234567", "Ana", ChannelKind.WhatsApp, "hola", null, null, null, Now));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        _contacts.Verify(c => c.AddAsync(It.IsAny<Contact>(), It.IsAny<CancellationToken>()), Times.Never);
        _messages.Verify(m => m.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_StatusUpdate_ForUnknownExternalId_IsIgnoredWithoutError()
    {
        SetupItems(new InboundStatusUpdate("phone-1", "wamid.1", ChannelKind.WhatsApp, MessageStatus.Delivered, null, Now));

        var exception = await Record.ExceptionAsync(() => _sut.ProcessAsync(CreateEvent(), CancellationToken.None));

        Assert.Null(exception);
        _messages.Verify(m => m.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_StatusUpdate_Delivered_MarksMessageAndStagesEvent()
    {
        var message = Message.OutboundText(TenantId, Guid.NewGuid(), "hola", Guid.NewGuid(), MessageCategory.Service, Now.AddMinutes(-1));
        message.MarkSent("wamid.1", Now.AddMinutes(-1));
        _messages.Setup(m => m.FindByExternalIdAsync("wamid.1", It.IsAny<CancellationToken>())).ReturnsAsync(message);
        SetupItems(new InboundStatusUpdate("phone-1", "wamid.1", ChannelKind.WhatsApp, MessageStatus.Delivered, null, Now));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        Assert.Equal(MessageStatus.Delivered, message.Status);
        _outboxWriter.Verify(
            w => w.StageAsync(TenantId, nameof(Message), message.Id, "message.delivered", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_StatusUpdate_Read_MarksMessageAndStagesEvent()
    {
        var message = Message.OutboundText(TenantId, Guid.NewGuid(), "hola", Guid.NewGuid(), MessageCategory.Service, Now.AddMinutes(-1));
        message.MarkSent("wamid.1", Now.AddMinutes(-1));
        _messages.Setup(m => m.FindByExternalIdAsync("wamid.1", It.IsAny<CancellationToken>())).ReturnsAsync(message);
        SetupItems(new InboundStatusUpdate("phone-1", "wamid.1", ChannelKind.WhatsApp, MessageStatus.Read, null, Now));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        Assert.Equal(MessageStatus.Read, message.Status);
        _outboxWriter.Verify(
            w => w.StageAsync(TenantId, nameof(Message), message.Id, "message.read", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_StatusUpdate_Failed_MarksMessageWithErrorCodeAndStagesEvent()
    {
        var message = Message.OutboundText(TenantId, Guid.NewGuid(), "hola", Guid.NewGuid(), MessageCategory.Service, Now.AddMinutes(-1));
        message.MarkSent("wamid.1", Now.AddMinutes(-1));
        _messages.Setup(m => m.FindByExternalIdAsync("wamid.1", It.IsAny<CancellationToken>())).ReturnsAsync(message);
        SetupItems(new InboundStatusUpdate("phone-1", "wamid.1", ChannelKind.WhatsApp, MessageStatus.Failed, "131047", Now));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, message.Status);
        Assert.Equal("131047", message.ErrorCode);
        _outboxWriter.Verify(
            w => w.StageAsync(TenantId, nameof(Message), message.Id, "message.failed", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_SavesEverythingInOneUnitOfWork()
    {
        SetupItems(
            new InboundMessage("phone-1", "wamid.1", "573001234567", "Ana", ChannelKind.WhatsApp, "hola", null, null, null, Now),
            new InboundMessage("phone-1", "wamid.2", "573001234567", "Ana", ChannelKind.WhatsApp, "otra vez", null, null, null, Now.AddSeconds(1)));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _messages.Verify(m => m.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_StagesMessageReceivedEvent()
    {
        SetupItems(new InboundMessage("phone-1", "wamid.1", "573001234567", "Ana", ChannelKind.WhatsApp, "hola", null, null, null, Now));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        _outboxWriter.Verify(
            w => w.StageAsync(TenantId, nameof(Message), It.IsAny<Guid>(), "message.received", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewConversation_AlsoStagesConversationOpenedEvent()
    {
        SetupItems(new InboundMessage("phone-1", "wamid.1", "573001234567", "Ana", ChannelKind.WhatsApp, "hola", null, null, null, Now));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        _outboxWriter.Verify(
            w => w.StageAsync(TenantId, nameof(Conversation), It.IsAny<Guid>(), "conversation.opened", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ExistingConversation_DoesNotStageConversationOpenedAgain()
    {
        var contact = Contact.Create(TenantId, "Ana", Now, phone: "573001234567");
        var conversation = Conversation.Open(TenantId, contact.Id, ChannelAccountId, Now.AddDays(-1));
        _contacts.Setup(c => c.FindByPhoneAsync(TenantId, "573001234567", It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        _conversations
            .Setup(c => c.FindOpenByContactAndAccountAsync(contact.Id, ChannelAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        SetupItems(new InboundMessage("phone-1", "wamid.1", "573001234567", "Ana", ChannelKind.WhatsApp, "hola", null, null, null, Now));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        _outboxWriter.Verify(
            w => w.StageAsync(It.IsAny<Guid>(), nameof(Conversation), It.IsAny<Guid>(), "conversation.opened", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithMediaExternalId_DownloadsAndStoresMedia()
    {
        var content = new MemoryStream("bytes"u8.ToArray());
        _adapter
            .Setup(a => a.DownloadMediaAsync(FakeAccount, "media-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((content, "image/jpeg"));
        SetupItems(new InboundMessage("phone-1", "wamid.1", "573001234567", "Ana", ChannelKind.WhatsApp, null, "media-1", "image/jpeg", null, Now));

        await _sut.ProcessAsync(CreateEvent(), CancellationToken.None);

        _mediaStorage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        _messages.Verify(
            m => m.AddAsync(It.Is<Message>(x => x.MediaKey != null && x.MediaMime == "image/jpeg"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenMediaDownloadFails_KeepsTheMessageAndStagesMediaFailed()
    {
        _adapter
            .Setup(a => a.DownloadMediaAsync(FakeAccount, "media-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelSendException("media_error", isTransient: false, "download failed"));
        SetupItems(new InboundMessage("phone-1", "wamid.1", "573001234567", "Ana", ChannelKind.WhatsApp, null, "media-1", "image/jpeg", null, Now));

        var exception = await Record.ExceptionAsync(() => _sut.ProcessAsync(CreateEvent(), CancellationToken.None));

        Assert.Null(exception);
        _messages.Verify(m => m.AddAsync(It.Is<Message>(x => x.MediaKey == null), It.IsAny<CancellationToken>()), Times.Once);
        _mediaStorage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxWriter.Verify(
            w => w.StageAsync(TenantId, nameof(Message), It.IsAny<Guid>(), "message.media_failed", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void SetupItems(params InboundItem[] items)
        => _adapter.Setup(a => a.ParseInbound(It.IsAny<string>())).Returns(items);

    private static InboundWebhookEvent CreateEvent()
        => InboundWebhookEvent.Receive(TenantId, ChannelAccountId, ChannelKind.WhatsApp, "{}", new string('a', 64), Now);
}
