using Orbita.Domain.Billing;

namespace Orbita.Application.Billing;

/// <summary>
/// One payment rail behind ORB-A12's plan/subscription lifecycle —
/// <c>StripePaymentProvider</c> and <c>WompiPaymentProvider</c> (both in
/// Infrastructure) implement this. <c>SubscriptionService</c> picks one by
/// <c>Tenant.CountryCode</c> ("CO" → Wompi, everything else → Stripe) out of an
/// injected <see cref="IEnumerable{T}"/> keyed by <see cref="Kind"/>.
///
/// Stripe and Wompi have genuinely different capabilities — Stripe manages
/// subscriptions, proration and dunning natively; Wompi only has one-off transactions
/// against a reusable tokenized payment source, with no subscription object at all.
/// This port is shaped around what <c>SubscriptionService</c> needs from either one,
/// not around Stripe's richer model — see <c>WompiPaymentProvider</c>'s class comment
/// for how it maps onto that narrower reality (and what still needs a scheduler before
/// it's production-ready).
/// </summary>
public interface IPaymentProvider
{
    PaymentProvider Kind { get; }

    /// <param name="paymentMethodToken">
    /// An opaque token/id the frontend obtained from this provider's own client-side
    /// SDK after collecting card details (a Stripe.js PaymentMethod id, or a Wompi
    /// payment_source_id) — raw card data must never reach this API.
    /// </param>
    Task<string> CreateCustomerAsync(string email, string name, string paymentMethodToken, CancellationToken cancellationToken);

    Task<ProviderSubscriptionState> CreateSubscriptionAsync(string providerCustomerId, Plan plan, CancellationToken cancellationToken);

    Task<ProviderSubscriptionState> ChangeSubscriptionAsync(string providerSubscriptionId, Plan newPlan, CancellationToken cancellationToken);

    Task CancelSubscriptionAsync(string providerSubscriptionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string providerCustomerId, CancellationToken cancellationToken);

    /// <summary>
    /// Null means the signature checked out but the event isn't one that changes a
    /// subscription's status (e.g. a Stripe event about something other than a
    /// subscription) — a legitimate no-op, not an error.
    /// </summary>
    /// <exception cref="InvalidWebhookSignatureException">The signature doesn't check out.</exception>
    ProviderSubscriptionState? ParseWebhookEvent(string payload, string signatureHeader);
}
