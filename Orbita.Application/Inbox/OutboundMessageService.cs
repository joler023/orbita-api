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

        var template = await unitOfWork.QueryInTenantScopeAsync(
            ct => messageTemplates.GetByIdAsync(request.TemplateId, ct),
            cancellationToken);
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

        var message = await unitOfWork.QueryInTenantScopeAsync(
            ct => messages.GetByIdAsync(messageId, ct),
            cancellationToken);
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
    //
    // The read has to run inside QueryInTenantScopeAsync, not as a plain repository call:
    // `app.tenant_id` is set with SET LOCAL semantics, so outside a transaction that sets
    // it the RLS policy on `conversations` matches nothing and every send answers 404 on
    // a conversation that is right there. The EF query filter alone does not show this —
    // it passes, and then Postgres returns no rows anyway.
    //
    // The entities stay tracked by the same DbContext, so RegisterOutbound below still
    // reaches the later SaveChangesAsync.
    private async Task<(Conversation Conversation, ChannelAccount Account)> RequireSendableConversationAsync(
        Guid tenantId, Guid conversationId, bool requireOpenWindow, CancellationToken cancellationToken)
    {
        var loaded = await unitOfWork.QueryInTenantScopeAsync(
            async ct =>
            {
                var conversation = await conversations.GetByIdAsync(conversationId, ct);

                return conversation is null
                    ? (Conversation: (Conversation?)null, Account: (ChannelAccount?)null)
                    : (Conversation: conversation, Account: await channelAccounts.GetByIdAsync(conversation.ChannelAccountId, ct));
            },
            cancellationToken);

        if (loaded.Conversation is not { } conversation)
        {
            throw new ConversationNotFoundException();
        }

        if (loaded.Account is not { } account || account.TenantId != tenantId || account.Status != ChannelStatus.Connected)
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
