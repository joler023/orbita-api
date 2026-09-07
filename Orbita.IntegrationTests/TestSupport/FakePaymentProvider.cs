using Orbita.Application.Billing;
using Orbita.Domain.Billing;

namespace Orbita.IntegrationTests.TestSupport;

/// <summary>
/// Stands in for the real Stripe/Wompi adapters in tests — hitting either provider's
/// actual API would need live credentials this test suite doesn't have. Registered
/// once per <see cref="Domain.Billing.PaymentProvider"/> kind so SubscriptionService's
/// provider-selection logic (by Tenant.CountryCode) still has something to resolve.
/// </summary>
public sealed class FakePaymentProvider(PaymentProvider kind) : IPaymentProvider
{
    public PaymentProvider Kind { get; } = kind;

    public Task<string> CreateCustomerAsync(string email, string name, string paymentMethodToken, CancellationToken cancellationToken)
        => Task.FromResult($"fake-customer-{Guid.NewGuid():N}");

    public Task<ProviderSubscriptionState> CreateSubscriptionAsync(string providerCustomerId, Plan plan, CancellationToken cancellationToken)
        => Task.FromResult(new ProviderSubscriptionState($"fake-sub-{Guid.NewGuid():N}", SubscriptionStatus.Active, DateTimeOffset.UtcNow.AddMonths(1)));

    public Task<ProviderSubscriptionState> ChangeSubscriptionAsync(string providerSubscriptionId, Plan newPlan, CancellationToken cancellationToken)
        => Task.FromResult(new ProviderSubscriptionState(providerSubscriptionId, SubscriptionStatus.Active, DateTimeOffset.UtcNow.AddMonths(1)));

    public Task CancelSubscriptionAsync(string providerSubscriptionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string providerCustomerId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<InvoiceSummary>>([new InvoiceSummary("fake-inv-1", DateTimeOffset.UtcNow, 29m, "USD", "paid", "https://example.com/fake-inv-1")]);

    public ProviderSubscriptionState? ParseWebhookEvent(string payload, string signatureHeader) => null;
}
