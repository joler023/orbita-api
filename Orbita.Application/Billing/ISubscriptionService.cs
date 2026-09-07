using Orbita.Application.Identity;

namespace Orbita.Application.Billing;

/// <summary>Plan catalog and per-tenant subscription lifecycle (ORB-A12).</summary>
public interface ISubscriptionService
{
    /// <summary>Public catalog — no auth required.</summary>
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">Caller lacks the ManageBilling permission.</exception>
    Task<SubscriptionDto?> GetAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">Caller lacks the ManageBilling permission.</exception>
    /// <exception cref="PlanNotFoundException">No active plan with that id.</exception>
    /// <exception cref="SubscriptionAlreadyExistsException">The tenant already has a subscription — use ChangePlanAsync.</exception>
    Task<SubscriptionDto> SubscribeAsync(Guid tenantId, Guid callerUserId, SubscribeRequest request, CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">Caller lacks the ManageBilling permission.</exception>
    /// <exception cref="PlanNotFoundException">No active plan with that id.</exception>
    /// <exception cref="SubscriptionNotFoundException">The tenant has no subscription yet.</exception>
    Task<SubscriptionDto> ChangePlanAsync(Guid tenantId, Guid callerUserId, ChangePlanRequest request, CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">Caller lacks the ManageBilling permission.</exception>
    /// <exception cref="SubscriptionNotFoundException">The tenant has no subscription yet.</exception>
    Task CancelAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">Caller lacks the ManageBilling permission.</exception>
    /// <exception cref="SubscriptionNotFoundException">The tenant has no subscription yet.</exception>
    Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);
}
