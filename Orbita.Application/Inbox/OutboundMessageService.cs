using Orbita.Application.Channels;
using Orbita.Application.Identity;
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
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOutboundMessageService
{
    public async Task<MessageDto> SendTextAsync(Guid tenantId, Guid callerUserId, Guid conversationId, SendTextRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.SendMessages, cancellationToken);

        // conversations is RLS'd/query-filtered on the ambient tenant, so a null result
        // here already means "not found or not yours" — no separate tenant check needed.
        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new ConversationNotFoundException();

        // channel_accounts is not RLS'd (ORB-B01) — the tenant check here is the isolation.
        var account = await channelAccounts.GetByIdAsync(conversation.ChannelAccountId, cancellationToken);
        if (account is null || account.TenantId != tenantId || account.Status != ChannelStatus.Connected)
        {
            throw new ChannelNotConnectedException();
        }

        var now = timeProvider.GetUtcNow();
        var message = Message.OutboundText(tenantId, conversationId, request.Text, callerUserId, MessageCategory.Service, now);
        conversation.RegisterOutbound(now, request.Text, isHuman: true);
        await messages.AddAsync(message, cancellationToken);

        var job = OutboundMessageJob.Create(tenantId, message.Id, message.CreatedAt, account.Id, now);
        await outboundQueue.EnqueueAsync(job, cancellationToken);

        // No PII: ids and enums only (see CLAUDE.md, Outbox).
        await outboxWriter.StageAsync(
            tenantId, nameof(Message), message.Id, "message.queued",
            new { messageId = message.Id, conversationId, direction = message.Direction.ToString() },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MessageDto.From(message);
    }
}
