using Orbita.Application.Channels;
using Orbita.Application.Crm;
using Orbita.Application.Media;
using Orbita.Application.Outbox;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Inbox;

namespace Orbita.Application.Inbox;

/// <summary>Runs with the job's tenant already set as ambient — see OutboundMessageWorker.</summary>
public sealed class OutboundMessageDispatchService(
    IMessageRepository messages,
    IConversationRepository conversations,
    IContactRepository contacts,
    IChannelAccountRepository channelAccounts,
    IEnumerable<IChannelAdapter> adapters,
    IOutboundMessageRateLimiter rateLimiter,
    IMediaStorage mediaStorage,
    IOutboxWriter outboxWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOutboundMessageDispatchService
{
    private const short MaxAttempts = 5;

    public async Task DispatchAsync(OutboundMessageJob job, CancellationToken cancellationToken)
    {
        await rateLimiter.AcquireAsync(job.ChannelAccountId, cancellationToken);

        var message = await messages.GetByIdAsync(job.MessageId, cancellationToken)
            ?? throw new MessageNotFoundException();
        var conversation = await conversations.GetByIdAsync(message.ConversationId, cancellationToken)
            ?? throw new ConversationNotFoundException();
        var contact = await contacts.GetByIdAsync(conversation.ContactId, cancellationToken)
            ?? throw new ContactNotFoundException();
        var account = await channelAccounts.GetByIdAsync(job.ChannelAccountId, cancellationToken)
            ?? throw new ChannelAccountNotFoundException();
        var adapter = adapters.First(a => a.Kind == account.Kind);

        var now = timeProvider.GetUtcNow();
        try
        {
            var toExternalId = account.Kind == ChannelKind.WhatsApp ? contact.Phone : contact.InstagramUserId;
            if (string.IsNullOrEmpty(toExternalId))
            {
                throw new ChannelSendException("no_recipient_id", isTransient: false, "The contact has no identifier for this channel.");
            }

            var externalId = message.MediaKey is null
                ? await adapter.SendTextAsync(account, toExternalId, message.Body ?? string.Empty, cancellationToken)
                : await SendMediaAsync(account, adapter, toExternalId, message, cancellationToken);

            message.MarkSent(externalId, now);
            job.MarkSent();
            await outboxWriter.StageAsync(
                job.TenantId, nameof(Message), message.Id, "message.sent",
                new { messageId = message.Id, conversationId = conversation.Id },
                cancellationToken);
        }
        catch (ChannelSendException exception)
        {
            if (exception.ErrorCode is "190" or "131001")
            {
                account.MarkTokenExpired();
            }

            if (exception.IsTransient && job.Attempts < MaxAttempts)
            {
                job.ScheduleRetry(now, Backoff(job.Attempts), exception.Message);
            }
            else
            {
                message.MarkFailed(exception.ErrorCode);
                job.MarkFailed(exception.Message);
                await outboxWriter.StageAsync(
                    job.TenantId, nameof(Message), message.Id, "message.failed",
                    new { messageId = message.Id, conversationId = conversation.Id, errorCode = exception.ErrorCode },
                    cancellationToken);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> SendMediaAsync(ChannelAccount account, IChannelAdapter adapter, string toExternalId, Message message, CancellationToken cancellationToken)
    {
        var content = await mediaStorage.OpenReadAsync(message.MediaKey!, cancellationToken)
            ?? throw new ChannelSendException("media_not_found", isTransient: false, "The message's media file is missing from storage.");

        await using (content)
        {
            return await adapter.SendMediaAsync(account, toExternalId, content, message.MediaMime ?? "application/octet-stream", message.Body, cancellationToken);
        }
    }

    private static TimeSpan Backoff(short attempts) => TimeSpan.FromSeconds(30 * Math.Pow(2, attempts));
}
