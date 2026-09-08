using Orbita.Application.Channels;
using Orbita.Application.Identity;
using Orbita.Application.Media;
using Orbita.Application.Outbox;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Inbox;

public sealed class OutboundMessageService(
    IConversationRepository conversations,
    IChannelAccountRepository channelAccounts,
    IMessageRepository messages,
    IMessageTemplateRepository messageTemplates,
    IOutboundMessageQueue outboundQueue,
    IOutboxWriter outboxWriter,
    IMediaUrlSigner mediaUrlSigner,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOutboundMessageService
{
    private static readonly TimeSpan MediaLinkLifetime = TimeSpan.FromMinutes(15);

    public async Task<MessageDto> SendTextAsync(Guid tenantId, Guid callerUserId, Guid conversationId, SendTextRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.SendMessages, cancellationToken);

        var (conversation, account) = await RequireSendableConversationAsync(tenantId, conversationId, requireOpenWindow: true, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var message = Message.OutboundText(tenantId, conversationId, request.Text, callerUserId, MessageCategory.Service, now);
        conversation.RegisterOutbound(now, request.Text, isHuman: true);

        return await EnqueueAndSaveAsync(tenantId, conversationId, message, account.Id, cancellationToken);
    }

    public async Task<MessageDto> SendMediaAsync(Guid tenantId, Guid callerUserId, Guid conversationId, SendMediaRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.SendMessages, cancellationToken);

        if (!MediaKeyBuilder.BelongsTo(request.MediaKey, tenantId))
        {
            throw new MediaKeyNotFoundException();
        }

        var (conversation, account) = await RequireSendableConversationAsync(tenantId, conversationId, requireOpenWindow: true, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var message = Message.OutboundMedia(tenantId, conversationId, request.MediaKey, request.MediaMime, request.Caption, callerUserId, now);
        conversation.RegisterOutbound(now, request.Caption ?? "[multimedia]", isHuman: true);

        return await EnqueueAndSaveAsync(tenantId, conversationId, message, account.Id, cancellationToken);
    }

    public async Task<MessageDto> SendTemplateAsync(Guid tenantId, Guid callerUserId, Guid conversationId, SendTemplateRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.SendMessages, cancellationToken);

        // Templates exist specifically to write outside the window — no window check here.
        var (conversation, account) = await RequireSendableConversationAsync(tenantId, conversationId, requireOpenWindow: false, cancellationToken);

        var template = await messageTemplates.GetByIdAsync(request.TemplateId, cancellationToken);
        if (template is null || template.TenantId != tenantId)
        {
            throw new TemplateNotFoundException();
        }

        if (!template.IsApproved)
        {
            throw new TemplateNotApprovedException();
        }

        var renderedBody = template.Render(request.Variables);
        var now = timeProvider.GetUtcNow();
        var message = Message.OutboundTemplate(tenantId, conversationId, template.Id, renderedBody, request.Variables, template.Category, callerUserId, now);
        conversation.RegisterOutbound(now, renderedBody, isHuman: true);

        return await EnqueueAndSaveAsync(tenantId, conversationId, message, account.Id, cancellationToken);
    }

    /// <summary>ORB-B08: only a Failed message with a transient MetaErrorCatalog error can be retried — a permanent failure (e.g. 131047, outside window) would just fail again.</summary>
    public async Task<MessageDto> RetryAsync(Guid tenantId, Guid callerUserId, Guid messageId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.SendMessages, cancellationToken);

        var message = await messages.GetByIdAsync(messageId, cancellationToken);
        if (message is null || message.TenantId != tenantId)
        {
            throw new MessageNotFoundException();
        }

        if (message.Status != MessageStatus.Failed)
        {
            throw new MessageNotRetryableException();
        }

        var (isTransient, _) = MetaErrorCatalog.Describe(message.ErrorCode ?? "unknown");
        if (!isTransient)
        {
            throw new MessageNotRetryableException();
        }

        var (_, account) = await RequireSendableConversationAsync(tenantId, message.ConversationId, requireOpenWindow: false, cancellationToken);

        message.ResetForRetry();
        var job = OutboundMessageJob.Create(tenantId, message.Id, message.CreatedAt, account.Id, timeProvider.GetUtcNow());
        await outboundQueue.EnqueueAsync(job, cancellationToken);

        // No PII: ids and enums only (see CLAUDE.md, Outbox).
        await outboxWriter.StageAsync(
            tenantId, nameof(Message), message.Id, "message.queued",
            new { messageId = message.Id, conversationId = message.ConversationId, direction = message.Direction.ToString() },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MessageDto.From(message);
    }

    // conversations is RLS'd/query-filtered on the ambient tenant, so a null result here
    // already means "not found or not yours" — no separate tenant check needed.
    // channel_accounts is not RLS'd (ORB-B01) — the tenant check here is the isolation.
    private async Task<(Conversation Conversation, ChannelAccount Account)> RequireSendableConversationAsync(
        Guid tenantId, Guid conversationId, bool requireOpenWindow, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new ConversationNotFoundException();

        var account = await channelAccounts.GetByIdAsync(conversation.ChannelAccountId, cancellationToken);
        if (account is null || account.TenantId != tenantId || account.Status != ChannelStatus.Connected)
        {
            throw new ChannelNotConnectedException();
        }

        if (requireOpenWindow && !conversation.CanSendFreeForm(timeProvider.GetUtcNow()))
        {
            throw new ServiceWindowClosedException();
        }

        return (conversation, account);
    }

    private async Task<MessageDto> EnqueueAndSaveAsync(Guid tenantId, Guid conversationId, Message message, Guid channelAccountId, CancellationToken cancellationToken)
    {
        await messages.AddAsync(message, cancellationToken);

        var job = OutboundMessageJob.Create(tenantId, message.Id, message.CreatedAt, channelAccountId, timeProvider.GetUtcNow());
        await outboundQueue.EnqueueAsync(job, cancellationToken);

        // No PII: ids and enums only (see CLAUDE.md, Outbox).
        await outboxWriter.StageAsync(
            tenantId, nameof(Message), message.Id, "message.queued",
            new { messageId = message.Id, conversationId, direction = message.Direction.ToString() },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var mediaUrl = message.MediaKey is null
            ? null
            : $"/api/media/{mediaUrlSigner.CreateToken("get", message.MediaKey, timeProvider.GetUtcNow() + MediaLinkLifetime)}";
        return MessageDto.From(message, mediaUrl);
    }
}
