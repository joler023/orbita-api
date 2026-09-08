using Moq;
using Orbita.Application.Channels;
using Orbita.Application.Identity;
using Orbita.Application.Inbox;
using Orbita.Application.Media;
using Orbita.Application.Outbox;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Inbox;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class OutboundMessageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IChannelAccountRepository> _channelAccounts = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<IMessageTemplateRepository> _messageTemplates = new();
    private readonly Mock<IOutboundMessageQueue> _outboundQueue = new();
    private readonly Mock<IOutboxWriter> _outboxWriter = new();
    private readonly Mock<IMediaUrlSigner> _mediaUrlSigner = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly OutboundMessageService _sut;

    public OutboundMessageServiceTests()
    {
        _sut = new OutboundMessageService(
            _conversations.Object,
            _channelAccounts.Object,
            _messages.Object,
            _messageTemplates.Object,
            _outboundQueue.Object,
            _outboxWriter.Object,
            _mediaUrlSigner.Object,
            _authorization.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task SendTextAsync_PersistsQueuedMessageJobAndOutboxBeforeReturning()
    {
        var conversation = Conversation.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1));
        conversation.RegisterInbound(Now.AddHours(-1), "hola");
        var account = ChannelAccount.ConnectWhatsApp(TenantId, "phone-1", "waba-1", "Acme", null, "local://a", null, Now.AddDays(-1));
        account.MarkConnected(Now.AddDays(-1));
        _conversations.Setup(c => c.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _channelAccounts.Setup(a => a.GetByIdAsync(conversation.ChannelAccountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var result = await _sut.SendTextAsync(TenantId, CallerId, conversation.Id, new SendTextRequest("hola de vuelta"), CancellationToken.None);

        Assert.Equal("hola de vuelta", result.Body);
        Assert.Equal(MessageStatus.Queued, result.Status);
        _messages.Verify(m => m.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboundQueue.Verify(q => q.EnqueueAsync(It.Is<OutboundMessageJob>(j => j.ChannelAccountId == account.Id), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(w => w.StageAsync(TenantId, nameof(Message), It.IsAny<Guid>(), "message.queued", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendTextAsync_ByViewer_Forbidden()
    {
        _authorization
            .Setup(a => a.EnsurePermissionAsync(TenantId, CallerId, Permission.SendMessages, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Viewer cannot send messages."));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.SendTextAsync(TenantId, CallerId, Guid.NewGuid(), new SendTextRequest("hola"), CancellationToken.None));

        _messages.Verify(m => m.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendTextAsync_ForAnUnknownConversation_ThrowsNotFound()
    {
        _conversations.Setup(c => c.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Conversation?)null);

        await Assert.ThrowsAsync<ConversationNotFoundException>(
            () => _sut.SendTextAsync(TenantId, CallerId, Guid.NewGuid(), new SendTextRequest("hola"), CancellationToken.None));
    }

    [Fact]
    public async Task SendTextAsync_WhenChannelDisconnected_Throws()
    {
        var conversation = Conversation.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1));
        var account = ChannelAccount.ConnectWhatsApp(TenantId, "phone-1", "waba-1", "Acme", null, "local://a", null, Now.AddDays(-1));
        // Never MarkConnected: stays PendingVerification.
        _conversations.Setup(c => c.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _channelAccounts.Setup(a => a.GetByIdAsync(conversation.ChannelAccountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        await Assert.ThrowsAsync<ChannelNotConnectedException>(
            () => _sut.SendTextAsync(TenantId, CallerId, conversation.Id, new SendTextRequest("hola"), CancellationToken.None));

        _messages.Verify(m => m.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendTextAsync_ForAChannelAccountOfAnotherTenant_Throws()
    {
        var conversation = Conversation.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1));
        var account = ChannelAccount.ConnectWhatsApp(Guid.NewGuid(), "phone-1", "waba-1", "Acme", null, "local://a", null, Now.AddDays(-1));
        account.MarkConnected(Now.AddDays(-1));
        _conversations.Setup(c => c.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _channelAccounts.Setup(a => a.GetByIdAsync(conversation.ChannelAccountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        await Assert.ThrowsAsync<ChannelNotConnectedException>(
            () => _sut.SendTextAsync(TenantId, CallerId, conversation.Id, new SendTextRequest("hola"), CancellationToken.None));
    }

    [Fact]
    public async Task SendTextAsync_OutsideWindow_Throws()
    {
        var conversation = Conversation.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2));
        conversation.RegisterInbound(Now.AddDays(-2), "hola"); // window already lapsed by Now
        var account = ChannelAccount.ConnectWhatsApp(TenantId, "phone-1", "waba-1", "Acme", null, "local://a", null, Now.AddDays(-2));
        account.MarkConnected(Now.AddDays(-2));
        _conversations.Setup(c => c.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _channelAccounts.Setup(a => a.GetByIdAsync(conversation.ChannelAccountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        await Assert.ThrowsAsync<ServiceWindowClosedException>(
            () => _sut.SendTextAsync(TenantId, CallerId, conversation.Id, new SendTextRequest("hola"), CancellationToken.None));

        _messages.Verify(m => m.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendTemplateAsync_OutsideWindow_StillSucceeds()
    {
        var (conversation, account) = SetUpClosedWindowConversation();
        var template = MessageTemplate.Create(TenantId, account.Id, "greeting", MessageCategory.Utility, "es", "Hola {{1}}", Now);
        template.ApplyMetaStatus(TemplateStatus.Approved, null, Now);
        _messageTemplates.Setup(t => t.GetByIdAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var result = await _sut.SendTemplateAsync(TenantId, CallerId, conversation.Id, new SendTemplateRequest(template.Id, ["Ana"]), CancellationToken.None);

        Assert.Equal("Hola Ana", result.Body);
        Assert.Equal(MessageCategory.Utility, result.Category);
    }

    [Fact]
    public async Task SendTemplateAsync_WithPendingTemplate_Throws()
    {
        var (conversation, account) = SetUpClosedWindowConversation();
        var template = MessageTemplate.Create(TenantId, account.Id, "greeting", MessageCategory.Utility, "es", "Hola {{1}}", Now);
        _messageTemplates.Setup(t => t.GetByIdAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        await Assert.ThrowsAsync<TemplateNotApprovedException>(
            () => _sut.SendTemplateAsync(TenantId, CallerId, conversation.Id, new SendTemplateRequest(template.Id, ["Ana"]), CancellationToken.None));
    }

    [Fact]
    public async Task SendTemplateAsync_ForAnUnknownTemplate_ThrowsNotFound()
    {
        var (conversation, _) = SetUpClosedWindowConversation();
        _messageTemplates.Setup(t => t.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((MessageTemplate?)null);

        await Assert.ThrowsAsync<TemplateNotFoundException>(
            () => _sut.SendTemplateAsync(TenantId, CallerId, conversation.Id, new SendTemplateRequest(Guid.NewGuid(), []), CancellationToken.None));
    }

    private (Conversation Conversation, ChannelAccount Account) SetUpClosedWindowConversation()
    {
        var conversation = Conversation.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2));
        conversation.RegisterInbound(Now.AddDays(-2), "hola");
        var account = ChannelAccount.ConnectWhatsApp(TenantId, "phone-1", "waba-1", "Acme", null, "local://a", null, Now.AddDays(-2));
        account.MarkConnected(Now.AddDays(-2));
        _conversations.Setup(c => c.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _channelAccounts.Setup(a => a.GetByIdAsync(conversation.ChannelAccountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        return (conversation, account);
    }
}
