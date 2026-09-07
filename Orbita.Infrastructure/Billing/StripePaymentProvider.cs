using Microsoft.Extensions.Configuration;
using Orbita.Application.Billing;
using Stripe;
using DomainPlan = Orbita.Domain.Billing.Plan;
using DomainPaymentProvider = Orbita.Domain.Billing.PaymentProvider;
using DomainSubscriptionStatus = Orbita.Domain.Billing.SubscriptionStatus;
using StripeSubscriptionService = Stripe.SubscriptionService;

namespace Orbita.Infrastructure.Billing;

/// <summary>
/// The international rail (ORB-A12), via the official Stripe.net SDK. Needs
/// `Billing:Stripe:SecretKey` and `Billing:Stripe:WebhookSecret` configured before any
/// call here will succeed against a real (even test-mode) Stripe account — both are
/// empty placeholders in appsettings until a real account is connected. See
/// CLAUDE.md's Billing section for what else is needed to go live (a Stripe Price id
/// recorded on each Plan via <see cref="DomainPlan.SetStripePriceId"/>).
/// </summary>
public sealed class StripePaymentProvider : IPaymentProvider
{
    private readonly string _webhookSecret;

    public StripePaymentProvider(IConfiguration configuration)
    {
        StripeConfiguration.ApiKey = configuration["Billing:Stripe:SecretKey"] ?? string.Empty;
        _webhookSecret = configuration["Billing:Stripe:WebhookSecret"] ?? string.Empty;
    }

    public DomainPaymentProvider Kind => DomainPaymentProvider.Stripe;

    public async Task<string> CreateCustomerAsync(string email, string name, string paymentMethodToken, CancellationToken cancellationToken)
    {
        var service = new CustomerService();
        var customer = await service.CreateAsync(
            new CustomerCreateOptions
            {
                Email = email,
                Name = name,
                PaymentMethod = paymentMethodToken,
                InvoiceSettings = new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = paymentMethodToken },
            },
            cancellationToken: cancellationToken);

        return customer.Id;
    }

    public async Task<ProviderSubscriptionState> CreateSubscriptionAsync(string providerCustomerId, DomainPlan plan, CancellationToken cancellationToken)
    {
        var service = new StripeSubscriptionService();
        var subscription = await service.CreateAsync(
            new SubscriptionCreateOptions
            {
                Customer = providerCustomerId,
                Items = [new SubscriptionItemOptions { Price = RequireStripePriceId(plan) }],
            },
            cancellationToken: cancellationToken);

        return ToState(subscription);
    }

    public async Task<ProviderSubscriptionState> ChangeSubscriptionAsync(string providerSubscriptionId, DomainPlan newPlan, CancellationToken cancellationToken)
    {
        var service = new StripeSubscriptionService();
        var existing = await service.GetAsync(providerSubscriptionId, cancellationToken: cancellationToken);
        var currentItemId = existing.Items.Data[0].Id;

        var updated = await service.UpdateAsync(
            providerSubscriptionId,
            new SubscriptionUpdateOptions
            {
                Items = [new SubscriptionItemOptions { Id = currentItemId, Price = RequireStripePriceId(newPlan) }],
                // Stripe computes and bills the prorated difference automatically —
                // this is the acceptance criterion "cambio de plan con prorrateo".
                ProrationBehavior = "create_prorations",
            },
            cancellationToken: cancellationToken);

        return ToState(updated);
    }

    public async Task CancelSubscriptionAsync(string providerSubscriptionId, CancellationToken cancellationToken)
    {
        var service = new StripeSubscriptionService();
        await service.CancelAsync(providerSubscriptionId, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string providerCustomerId, CancellationToken cancellationToken)
    {
        var service = new InvoiceService();
        var invoices = await service.ListAsync(
            new InvoiceListOptions { Customer = providerCustomerId, Limit = 100 },
            cancellationToken: cancellationToken);

        return invoices
            .Select(invoice => new InvoiceSummary(
                invoice.Id,
                invoice.Created,
                invoice.AmountDue / 100m, // Stripe amounts are in the currency's smallest unit (cents).
                invoice.Currency.ToUpperInvariant(),
                invoice.Status,
                invoice.HostedInvoiceUrl))
            .ToList();
    }

    public ProviderSubscriptionState? ParseWebhookEvent(string payload, string signatureHeader)
    {
        Event stripeEvent;
        try
        {
            // throwOnApiVersionMismatch: false — this API doesn't pin a specific
            // Stripe API version yet, so rejecting events whose api_version doesn't
            // match Stripe.net's bundled expectation would reject every real event.
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _webhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            throw new InvalidWebhookSignatureException();
        }

        // Only customer.subscription.* events carry a full Subscription object with a
        // status; everything else (invoice events, payment method events, ...) is not
        // this method's concern and is a legitimate no-op.
        return stripeEvent.Data.Object is Stripe.Subscription subscription ? ToState(subscription) : null;
    }

    private static string RequireStripePriceId(DomainPlan plan)
        => plan.StripePriceId ?? throw new InvalidOperationException($"Plan '{plan.Code}' has no Stripe price id configured yet.");

    private static ProviderSubscriptionState ToState(Stripe.Subscription subscription)
    {
        // Stripe tracks the billing period per subscription item, not on the
        // subscription itself, since a subscription can hold items on different
        // cycles — Plans only ever create a single-item subscription, so the first
        // item's period is the subscription's period for Orbita's purposes.
        var currentPeriodEnd = subscription.Items.Data.Count > 0
            ? new DateTimeOffset(subscription.Items.Data[0].CurrentPeriodEnd, TimeSpan.Zero)
            : (DateTimeOffset?)null;

        return new ProviderSubscriptionState(subscription.Id, MapStatus(subscription.Status), currentPeriodEnd);
    }

    private static DomainSubscriptionStatus MapStatus(string stripeStatus) => stripeStatus switch
    {
        "trialing" => DomainSubscriptionStatus.Trialing,
        "active" => DomainSubscriptionStatus.Active,
        "past_due" or "unpaid" or "incomplete" => DomainSubscriptionStatus.PastDue,
        "canceled" or "incomplete_expired" => DomainSubscriptionStatus.Canceled,
        _ => DomainSubscriptionStatus.Active,
    };
}
