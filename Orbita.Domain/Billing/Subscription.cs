using Orbita.Domain.Common;

namespace Orbita.Domain.Billing;

/// <summary>
/// A tenant's subscription to a <see cref="Plan"/> (ORB-A12). Deliberately NOT tenant
/// query-filtered/RLS'd, unlike most tenant-scoped entities — the same exception
/// already made for InvitationToken/PasswordResetToken/RefreshToken: a payment
/// provider's webhook arrives with only <see cref="ProviderSubscriptionId"/>, no
/// ambient tenant, and Postgres RLS returns zero rows for every query when no tenant
/// is set in the session (see CLAUDE.md's Authentication section on the missing JWT
/// tenant claim) — so a table that must be looked up by an external opaque id before
/// any tenant is known cannot be RLS'd. <see cref="TenantId"/> is still checked
/// explicitly by application code wherever it matters (see SubscriptionService), the
/// same defense-in-depth already used for InvitationToken lookups.
/// </summary>
public sealed class Subscription : Entity
{
    private Subscription(
        Guid id,
        Guid tenantId,
        Guid planId,
        PaymentProvider provider,
        string providerCustomerId,
        string? providerSubscriptionId,
        SubscriptionStatus status,
        DateTimeOffset? currentPeriodEnd,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        : base(id)
    {
        TenantId = tenantId;
        PlanId = planId;
        Provider = provider;
        ProviderCustomerId = providerCustomerId;
        ProviderSubscriptionId = providerSubscriptionId;
        Status = status;
        CurrentPeriodEnd = currentPeriodEnd;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid TenantId { get; }

    public Guid PlanId { get; private set; }

    public PaymentProvider Provider { get; }

    /// <summary>The provider's id for the tenant as a customer (Stripe Customer id / Wompi customer email+id pairing).</summary>
    public string ProviderCustomerId { get; }

    /// <summary>
    /// Stripe's Subscription id, or — for Wompi, which has no subscription object —
    /// the reusable payment_source_id future charges are made against.
    /// </summary>
    public string? ProviderSubscriptionId { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset? CurrentPeriodEnd { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Subscription Create(
        Guid tenantId,
        Guid planId,
        PaymentProvider provider,
        string providerCustomerId,
        string? providerSubscriptionId,
        SubscriptionStatus status,
        DateTimeOffset? currentPeriodEnd,
        DateTimeOffset now)
        => new(Guid.NewGuid(), tenantId, planId, provider, providerCustomerId, providerSubscriptionId, status, currentPeriodEnd, now, now);

    public void ChangePlan(Guid newPlanId, DateTimeOffset now)
    {
        PlanId = newPlanId;
        UpdatedAt = now;
    }

    /// <summary>Applied from a provider's response or webhook — never set piecemeal from application code.</summary>
    public void ApplyProviderState(string? providerSubscriptionId, SubscriptionStatus status, DateTimeOffset? currentPeriodEnd, DateTimeOffset now)
    {
        if (providerSubscriptionId is not null)
        {
            ProviderSubscriptionId = providerSubscriptionId;
        }

        Status = status;
        CurrentPeriodEnd = currentPeriodEnd;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = SubscriptionStatus.Canceled;
        UpdatedAt = now;
    }
}
