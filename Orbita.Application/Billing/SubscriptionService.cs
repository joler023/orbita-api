using Orbita.Application.Identity;
using Orbita.Domain.Billing;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;

namespace Orbita.Application.Billing;

public sealed class SubscriptionService(
    IPlanRepository planRepository,
    ISubscriptionRepository subscriptionRepository,
    ITenantRepository tenantRepository,
    IUserRepository userRepository,
    ITenantAuthorizationService authorizationService,
    IEnumerable<IPaymentProvider> paymentProviders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ISubscriptionService
{
    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await planRepository.GetAllActiveAsync(cancellationToken);
        return plans.Select(ToDto).ToList();
    }

    public async Task<SubscriptionDto?> GetAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageBilling, cancellationToken);

        var subscription = await subscriptionRepository.GetByTenantIdAsync(tenantId, cancellationToken);
        return subscription is null ? null : await ToDtoAsync(subscription, cancellationToken);
    }

    public async Task<SubscriptionDto> SubscribeAsync(Guid tenantId, Guid callerUserId, SubscribeRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageBilling, cancellationToken);

        if (await subscriptionRepository.GetByTenantIdAsync(tenantId, cancellationToken) is not null)
        {
            throw new SubscriptionAlreadyExistsException();
        }

        var plan = await planRepository.GetByIdAsync(request.PlanId, cancellationToken)
            ?? throw new PlanNotFoundException();
        var tenant = await tenantRepository.GetByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found.");
        var owner = await userRepository.GetByIdAsync(callerUserId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{callerUserId}' not found.");

        var provider = ResolveProviderForTenant(tenant);
        var providerCustomerId = await provider.CreateCustomerAsync(owner.Email, owner.FullName, request.PaymentMethodToken, cancellationToken);
        var providerState = await provider.CreateSubscriptionAsync(providerCustomerId, plan, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var subscription = Subscription.Create(
            tenantId,
            plan.Id,
            provider.Kind,
            providerCustomerId,
            providerState.ProviderSubscriptionId,
            providerState.Status,
            providerState.CurrentPeriodEnd,
            now);
        await subscriptionRepository.AddAsync(subscription, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(subscription, plan);
    }

    public async Task<SubscriptionDto> ChangePlanAsync(Guid tenantId, Guid callerUserId, ChangePlanRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageBilling, cancellationToken);

        var subscription = await RequireSubscriptionAsync(tenantId, cancellationToken);
        var newPlan = await planRepository.GetByIdAsync(request.PlanId, cancellationToken)
            ?? throw new PlanNotFoundException();

        if (subscription.ProviderSubscriptionId is null)
        {
            throw new InvalidOperationException("Subscription has no provider subscription id yet.");
        }

        var provider = ResolveProvider(subscription.Provider);
        var providerState = await provider.ChangeSubscriptionAsync(subscription.ProviderSubscriptionId, newPlan, cancellationToken);

        var now = timeProvider.GetUtcNow();
        subscription.ChangePlan(newPlan.Id, now);
        subscription.ApplyProviderState(providerState.ProviderSubscriptionId, providerState.Status, providerState.CurrentPeriodEnd, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(subscription, newPlan);
    }

    public async Task CancelAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageBilling, cancellationToken);
        var subscription = await RequireSubscriptionAsync(tenantId, cancellationToken);

        if (subscription.ProviderSubscriptionId is not null)
        {
            var provider = ResolveProvider(subscription.Provider);
            await provider.CancelSubscriptionAsync(subscription.ProviderSubscriptionId, cancellationToken);
        }

        subscription.Cancel(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageBilling, cancellationToken);
        var subscription = await RequireSubscriptionAsync(tenantId, cancellationToken);

        var provider = ResolveProvider(subscription.Provider);
        return await provider.ListInvoicesAsync(subscription.ProviderCustomerId, cancellationToken);
    }

    private async Task<Subscription> RequireSubscriptionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByTenantIdAsync(tenantId, cancellationToken);

        // Defense in depth even though the repository already filters by tenantId
        // explicitly — Subscription isn't RLS'd, see its class comment.
        if (subscription is null || subscription.TenantId != tenantId)
        {
            throw new SubscriptionNotFoundException();
        }

        return subscription;
    }

    private IPaymentProvider ResolveProviderForTenant(Tenant tenant)
        => ResolveProvider(tenant.CountryCode == "CO" ? PaymentProvider.Wompi : PaymentProvider.Stripe);

    private IPaymentProvider ResolveProvider(PaymentProvider kind)
        => paymentProviders.SingleOrDefault(p => p.Kind == kind)
            ?? throw new InvalidOperationException($"No IPaymentProvider registered for '{kind}'.");

    private async Task<SubscriptionDto> ToDtoAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var plan = await planRepository.GetByIdAsync(subscription.PlanId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{subscription.PlanId}' not found.");
        return ToDto(subscription, plan);
    }

    private static SubscriptionDto ToDto(Subscription subscription, Plan plan)
        => new(subscription.Id, plan.Id, plan.Code, plan.Name, subscription.Provider, subscription.Status, subscription.CurrentPeriodEnd);

    private static PlanDto ToDto(Plan plan)
        => new(plan.Id, plan.Code, plan.Name, plan.IncludedConversations, plan.IncludedAiCredits, plan.PriceAmount, plan.PriceCurrency);
}
