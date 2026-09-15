using Orbita.Domain.Channels;

namespace Orbita.Application.Channels;

/// <summary>
/// The no-loss boundary a raw webhook POST crosses before anything interprets it
/// (ORB-B02): verify → route to a <see cref="ChannelAccount"/> → de-dup → durably
/// enqueue. Deliberately free of EF/heavy DI (only ports, <see cref="TimeProvider"/> and
/// <see cref="Microsoft.Extensions.Logging.ILogger"/>) so it can be hosted in a Lambda
/// later without change.
/// </summary>
public interface IWebhookIngestionService
{
    /// <param name="channelAccountId">
    /// The account from the per-account callback route, or null for the app-level
    /// route (the account is then resolved from the payload's own external id).
    /// </param>
    /// <exception cref="Billing.InvalidWebhookSignatureException">The signature header doesn't match.</exception>
    Task<WebhookIngestionResult> IngestAsync(
        ChannelKind kind,
        Guid? channelAccountId,
        byte[] rawBody,
        string? signatureHeader,
        CancellationToken cancellationToken);
}
