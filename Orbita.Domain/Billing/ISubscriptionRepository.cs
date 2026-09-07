namespace Orbita.Domain.Billing;

public interface ISubscriptionRepository
{
    /// <summary>Explicitly filtered by tenantId in the query itself — see Subscription's class comment for why this isn't RLS'd.</summary>
    Task<Subscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Used to resolve which tenant a payment provider's webhook is about.</summary>
    Task<Subscription?> GetByProviderSubscriptionIdAsync(PaymentProvider provider, string providerSubscriptionId, CancellationToken cancellationToken);

    Task AddAsync(Subscription subscription, CancellationToken cancellationToken);
}
