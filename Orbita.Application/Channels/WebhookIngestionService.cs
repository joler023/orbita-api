using System.Text;
using Microsoft.Extensions.Logging;
using Orbita.Domain.Channels;

namespace Orbita.Application.Channels;

public sealed class WebhookIngestionService(
    IChannelAccountRepository channelAccountRepository,
    IWebhookSignatureVerifier signatureVerifier,
    IWebhookDeduplicator deduplicator,
    IInboundWebhookQueue queue,
    TimeProvider timeProvider,
    ILogger<WebhookIngestionService> logger) : IWebhookIngestionService
{
    public async Task<WebhookIngestionResult> IngestAsync(
        ChannelKind kind,
        Guid? channelAccountId,
        byte[] rawBody,
        string? signatureHeader,
        CancellationToken cancellationToken)
    {
        // Throws InvalidWebhookSignatureException on failure — the controller lets that
        // propagate to a 401, same as Stripe/Wompi's webhook verification.
        signatureVerifier.Verify(rawBody, signatureHeader);

        var account = await ResolveAccountAsync(kind, channelAccountId, rawBody, cancellationToken);
        if (account is null)
        {
            // Meta retries forever on anything but 200, and redelivering the same
            // unroutable payload won't make an account exist — the controller still
            // answers 200 for this case, it just never enqueues.
            logger.LogWarning("Inbound {Kind} webhook could not be routed to a channel account.", kind);
            return WebhookIngestionResult.Unroutable;
        }

        var hash = InboundWebhookEvent.Hash(rawBody);
        if (!await deduplicator.TryMarkSeenAsync(hash, cancellationToken))
        {
            return WebhookIngestionResult.Duplicate;
        }

        var payloadJson = Encoding.UTF8.GetString(rawBody);
        var webhookEvent = InboundWebhookEvent.Receive(account.TenantId, account.Id, kind, payloadJson, hash, timeProvider.GetUtcNow());
        await queue.EnqueueAsync(webhookEvent, cancellationToken);

        return WebhookIngestionResult.Accepted;
    }

    private async Task<ChannelAccount?> ResolveAccountAsync(
        ChannelKind kind, Guid? channelAccountId, byte[] rawBody, CancellationToken cancellationToken)
    {
        if (channelAccountId is { } id)
        {
            var account = await channelAccountRepository.GetByIdAsync(id, cancellationToken);
            if (account is null || account.Kind != kind)
            {
                return null;
            }

            // The route's id is trusted for storage, but the payload must actually be
            // about that account — otherwise a stale/forged route id would silently
            // attribute someone else's messages to the wrong tenant.
            if (kind == ChannelKind.WhatsApp && WhatsAppPayloadInspector.ReadPhoneNumberId(rawBody) != account.ExternalId)
            {
                return null;
            }

            return account;
        }

        var externalId = kind switch
        {
            ChannelKind.WhatsApp => WhatsAppPayloadInspector.ReadPhoneNumberId(rawBody),
            ChannelKind.Instagram => InstagramPayloadInspector.ReadAccountId(rawBody),
            _ => null,
        };

        return externalId is null
            ? null
            : await channelAccountRepository.FindByKindAndExternalIdAsync(kind, externalId, cancellationToken);
    }
}
