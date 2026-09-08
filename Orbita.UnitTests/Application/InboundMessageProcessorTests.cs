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

public sealed class InboundMessageProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ChannelAccountId = Guid.NewGuid();

    private readonly Mock<IChannelAdapter> _adapter = new();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly InboundMessageProcessor _sut;

    public InboundMessageProcessorTests()
    {
        _adapter.SetupGet(a => a.Kind).Returns(ChannelKind.WhatsApp);
        _messages.Setup(m => m.FindByExternalIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Message?)null);
        _conversations
            .Setup(c => c.FindOpenByContactAndAccountAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _contacts
            .Setup(c => c.FindByPhoneAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Contact?)null);

        _sut = new InboundMessageProcessor(
            new[] { _adapter.Object },
            _contacts.Object,
            _conversations.Object,
            _messages.Object,
            _outboxWriter.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
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
    public async Task ProcessAsync_StatusUpdate_IsIgnoredWithoutError()
    {
        SetupItems(new InboundStatusUpdate("phone-1", "wamid.1", ChannelKind.WhatsApp, MessageStatus.Delivered, null, Now));

        var exception = await Record.ExceptionAsync(() => _sut.ProcessAsync(CreateEvent(), CancellationToken.None));

        Assert.Null(exception);
        _messages.Verify(m => m.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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

    private void SetupItems(params InboundItem[] items)
        => _adapter.Setup(a => a.ParseInbound(It.IsAny<string>())).Returns(items);

    private static InboundWebhookEvent CreateEvent()
        => InboundWebhookEvent.Receive(TenantId, ChannelAccountId, ChannelKind.WhatsApp, "{}", new string('a', 64), Now);
}
