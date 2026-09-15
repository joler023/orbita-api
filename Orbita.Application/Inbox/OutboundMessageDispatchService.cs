using System.Text.Json;
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
    IMessageTemplateRepository messageTemplates,
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

        // Load, send, save — in that order, and the load in its own tenant-scoped
        // transaction. `messages`, `conversations` and `contacts` are all RLS'd, so a
        // plain read here finds nothing and every dispatch dies on MessageNotFound.
        //
        // Deliberately not one transaction around the whole method: the send in the
        // middle is an HTTP round trip to Meta, and holding a database transaction open
        // across it would tie a connection to the slowest thing in the pipeline. The
        // entities stay tracked by this scope's DbContext, so the SaveChangesAsync at the
        // end still persists what the send decided.
        var loaded = await unitOfWork.QueryInTenantScopeAsync(
            async ct =>
            {
                var message = await messages.GetByIdAsync(job.MessageId, ct)
                    ?? throw new MessageNotFoundException();
                var conversation = await conversations.GetByIdAsync(message.ConversationId, ct)
                    ?? throw new ConversationNotFoundException();
                var contact = await contacts.GetByIdAsync(conversation.ContactId, ct)
                    ?? throw new ContactNotFoundException();
                var account = await channelAccounts.GetByIdAsync(job.ChannelAccountId, ct)
                    ?? throw new ChannelAccountNotFoundException();
                var template = message.TemplateId is { } templateId
                    ? await messageTemplates.GetByIdAsync(templateId, ct) ?? throw new TemplateNotFoundException()
                    : null;

                return (Message: message, Conversation: conversation, Contact: contact, Account: account, Template: template);
            },
            cancellationToken);

        var (message, conversation, contact, account, template) = loaded;
        var adapter = adapters.First(a => a.Kind == account.Kind);

        var now = timeProvider.GetUtcNow();
        try
        {
            var toExternalId = account.Kind == ChannelKind.WhatsApp ? contact.Phone : contact.InstagramUserId;
            if (string.IsNullOrEmpty(toExternalId))
            {
                throw new ChannelSendException("no_recipient_id", isTransient: false, "The contact has no identifier for this channel.");
            }

            var externalId = template is not null
                ? await SendTemplateAsync(account, adapter, toExternalId, message, template, cancellationToken)
                : message.MediaKey is null
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

    private async Task<string> SendTemplateAsync(ChannelAccount account, IChannelAdapter adapter, string toExternalId, Message message, MessageTemplate template, CancellationToken cancellationToken)
    {
        var variables = JsonSerializer.Deserialize<List<string>>(message.TemplateVariablesJson ?? "[]") ?? [];

        return await adapter.SendTemplateAsync(account, toExternalId, template.MetaTemplateName, template.Language, variables, cancellationToken);
    }

    private static TimeSpan Backoff(short attempts) => TimeSpan.FromSeconds(30 * Math.Pow(2, attempts));
}
