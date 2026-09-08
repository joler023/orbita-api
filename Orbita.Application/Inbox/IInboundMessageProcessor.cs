using Orbita.Domain.Channels;

namespace Orbita.Application.Inbox;

/// <summary>
/// Turns one durably-queued webhook payload into contact/conversation/message state
/// (ORB-B03). Lets exceptions propagate — the caller (a background worker) is what
/// decides retry/dead-letter policy for the underlying <see cref="InboundWebhookEvent"/>.
/// </summary>
public interface IInboundMessageProcessor
{
    Task ProcessAsync(InboundWebhookEvent webhookEvent, CancellationToken cancellationToken);
}
