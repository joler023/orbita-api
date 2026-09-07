using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Billing;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionRepository(OrbitaDbContext dbContext) : ISubscriptionRepository
{
    public Task<Subscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken)
        => dbContext.Subscriptions.SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

    public Task<Subscription?> GetByProviderSubscriptionIdAsync(PaymentProvider provider, string providerSubscriptionId, CancellationToken cancellationToken)
        => dbContext.Subscriptions.SingleOrDefaultAsync(
            s => s.Provider == provider && s.ProviderSubscriptionId == providerSubscriptionId,
            cancellationToken);

    public async Task AddAsync(Subscription subscription, CancellationToken cancellationToken)
        => await dbContext.Subscriptions.AddAsync(subscription, cancellationToken);
}
