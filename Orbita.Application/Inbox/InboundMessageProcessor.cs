using Microsoft.Extensions.Logging;
using Orbita.Application.Channels;
using Orbita.Application.Media;
using Orbita.Application.Outbox;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Inbox;

public sealed class InboundMessageProcessor(
    IEnumerable<IChannelAdapter> adapters,
    IChannelAccountRepository channelAccounts,
    IContactRepository contacts,
    IConversationRepository conversations,
    IMessageRepository messages,
    IMediaStorage mediaStorage,
    IOutboxWriter outboxWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<InboundMessageProcessor> logger) : IInboundMessageProcessor
{
    public async Task ProcessAsync(InboundWebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        var adapter = adapters.FirstOrDefault(a => a.Kind == webhookEvent.Kind)
            ?? throw new InvalidOperationException($"No channel adapter registered for {webhookEvent.Kind}.");
        var account = await channelAccounts.GetByIdAsync(webhookEvent.ChannelAccountId, cancellationToken)
            ?? throw new InvalidOperationException($"Channel account {webhookEvent.ChannelAccountId} not found.");

        var items = adapter.ParseInbound(webhookEvent.PayloadJson);

        foreach (var item in items)
        {
            switch (item)
            {
                case InboundMessage message:
                    await ProcessMessageAsync(webhookEvent.TenantId, account, adapter, message, cancellationToken);
                    break;
                case InboundStatusUpdate statusUpdate:
                    await ProcessStatusUpdateAsync(webhookEvent.TenantId, statusUpdate, cancellationToken);
                    break;
            }
        }

        // One SaveChangesAsync for the whole event, not per message: a batch of several
        // messages in one webhook payload either lands together or not at all.
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>ORB-B08: delivery receipts for a message this tenant sent. An unknown external id is logged and skipped, never an error — Meta can report on messages this instance never enqueued.</summary>
    private async Task ProcessStatusUpdateAsync(Guid tenantId, InboundStatusUpdate item, CancellationToken cancellationToken)
    {
        var message = await messages.FindByExternalIdAsync(item.ExternalId, cancellationToken);
        if (message is null)
        {
            logger.LogWarning("Status update for unknown message external id {ExternalId}.", item.ExternalId);
            return;
        }

        var now = timeProvider.GetUtcNow();
        string eventType;
        switch (item.Status)
        {
            case MessageStatus.Delivered:
                message.MarkDelivered(now);
                eventType = "message.delivered";
                break;
            case MessageStatus.Read:
                message.MarkRead(now);
                eventType = "message.read";
                break;
            case MessageStatus.Failed:
                message.MarkFailed(item.ErrorCode ?? "unknown");
                eventType = "message.failed";
                break;
            default:
                // Meta also echoes "sent" status updates for messages we already know
                // are sent (MarkSent already ran in the dispatcher) — nothing to do.
                return;
        }

        await outboxWriter.StageAsync(
            tenantId, nameof(Message), message.Id, eventType,
            new { messageId = message.Id, conversationId = message.ConversationId },
            cancellationToken);
    }

    private async Task ProcessMessageAsync(Guid tenantId, ChannelAccount account, IChannelAdapter adapter, InboundMessage item, CancellationToken cancellationToken)
    {
        if (await messages.FindByExternalIdAsync(item.ExternalId, cancellationToken) is not null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var channel = item.Kind == ChannelKind.WhatsApp ? "whatsapp" : "instagram";

        var contact = item.Kind == ChannelKind.WhatsApp
            ? await contacts.FindByPhoneAsync(tenantId, item.SenderExternalId, cancellationToken)
            : await contacts.FindByInstagramUserIdAsync(tenantId, item.SenderExternalId, cancellationToken);

        if (contact is null)
        {
            contact = Contact.CreateFromChannel(tenantId, channel, item.SenderExternalId, item.SenderDisplayName, now);
            await contacts.AddAsync(contact, cancellationToken);
        }
        else
        {
            contact.RecordSeen(now);
        }

        var conversation = await conversations.FindOpenByContactAndAccountAsync(contact.Id, account.Id, cancellationToken);
        var isNewConversation = conversation is null;
        if (conversation is null)
        {
            conversation = Conversation.Open(tenantId, contact.Id, account.Id, now);
            await conversations.AddAsync(conversation, cancellationToken);
        }

        conversation.RegisterInbound(now, item.Body);

        var newMessage = Message.Inbound(tenantId, conversation.Id, item.ExternalId, item.Body, item.MediaMime, item.ReplyToExternalId, now);
        await messages.AddAsync(newMessage, cancellationToken);

        if (item.MediaExternalId is not null)
        {
            await TryStoreMediaAsync(tenantId, conversation.Id, account, adapter, newMessage, item.MediaExternalId, cancellationToken);
        }

        if (isNewConversation)
        {
            // No PII: ids and enums only — this event can sit unpublished for a while
            // and is read by handlers with no tenant scoping (see CLAUDE.md, Outbox).
            await outboxWriter.StageAsync(
                tenantId, nameof(Conversation), conversation.Id, "conversation.opened",
                new { conversationId = conversation.Id, contactId = contact.Id, channelAccountId = account.Id },
                cancellationToken);
        }

        await outboxWriter.StageAsync(
            tenantId, nameof(Message), newMessage.Id, "message.received",
            new { messageId = newMessage.Id, conversationId = conversation.Id, contactId = contact.Id, channelAccountId = account.Id, direction = newMessage.Direction.ToString() },
            cancellationToken);
    }

    /// <summary>A failed download never dead-letters the whole event — the message is kept without media (ORB-B06).</summary>
    private async Task TryStoreMediaAsync(
        Guid tenantId, Guid conversationId, ChannelAccount account, IChannelAdapter adapter, Message message, string mediaExternalId, CancellationToken cancellationToken)
    {
        try
        {
            var (content, mime) = await adapter.DownloadMediaAsync(account, mediaExternalId, cancellationToken);
            await using (content)
            {
                var key = MediaKeyBuilder.ForMessage(tenantId, conversationId, message.Id, mime);
                await mediaStorage.SaveAsync(key, content, cancellationToken);
                message.MarkMediaStored(key, mime);
            }
        }
        catch (ChannelSendException)
        {
            await outboxWriter.StageAsync(
                tenantId, nameof(Message), message.Id, "message.media_failed",
                new { messageId = message.Id, conversationId },
                cancellationToken);
        }
    }
}
