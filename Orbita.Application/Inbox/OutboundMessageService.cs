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
    IOutboundMessageQueue outboundQueue,
    IOutboxWriter outboxWriter,
    IMediaUrlSigner mediaUrlSigner,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOutboundMessageService
{
    public async Task<MessageDto> SendTextAsync(Guid tenantId, Guid callerUserId, Guid conversationId, SendTextRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.SendMessages, cancellationToken);

        var (conversation, account) = await RequireSendableConversationAsync(tenantId, conversationId, cancellationToken);

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

        var (conversation, account) = await RequireSendableConversationAsync(tenantId, conversationId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var message = Message.OutboundMedia(tenantId, conversationId, request.MediaKey, request.MediaMime, request.Caption, callerUserId, now);
        conversation.RegisterOutbound(now, request.Caption ?? "[multimedia]", isHuman: true);

        return await EnqueueAndSaveAsync(tenantId, conversationId, message, account.Id, cancellationToken);
    }

    // conversations is RLS'd/query-filtered on the ambient tenant, so a null result here
    // already means "not found or not yours" — no separate tenant check needed.
    // channel_accounts is not RLS'd (ORB-B01) — the tenant check here is the isolation.
    private async Task<(Conversation Conversation, ChannelAccount Account)> RequireSendableConversationAsync(
        Guid tenantId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new ConversationNotFoundException();

        var account = await channelAccounts.GetByIdAsync(conversation.ChannelAccountId, cancellationToken);
        if (account is null || account.TenantId != tenantId || account.Status != ChannelStatus.Connected)
        {
            throw new ChannelNotConnectedException();
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

    private static readonly TimeSpan MediaLinkLifetime = TimeSpan.FromMinutes(15);
}
