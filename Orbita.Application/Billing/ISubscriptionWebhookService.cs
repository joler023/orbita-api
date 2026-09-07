using Orbita.Domain.Billing;

namespace Orbita.Application.Billing;

/// <summary>Applies a payment provider's webhook event to the local Subscription it's about (ORB-A12).</summary>
public interface ISubscriptionWebhookService
{
    /// <exception cref="InvalidWebhookSignatureException">The signature doesn't check out.</exception>
    Task HandleWebhookAsync(PaymentProvider provider, string payload, string signatureHeader, CancellationToken cancellationToken);
}
