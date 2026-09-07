using Orbita.Domain.Billing;

namespace Orbita.UnitTests.Domain;

public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsTheGivenState()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var periodEnd = Now.AddMonths(1);

        var subscription = Subscription.Create(
            tenantId, planId, PaymentProvider.Stripe, "cus_123", "sub_123", SubscriptionStatus.Active, periodEnd, Now);

        Assert.Equal(tenantId, subscription.TenantId);
        Assert.Equal(planId, subscription.PlanId);
        Assert.Equal(PaymentProvider.Stripe, subscription.Provider);
        Assert.Equal("cus_123", subscription.ProviderCustomerId);
        Assert.Equal("sub_123", subscription.ProviderSubscriptionId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(periodEnd, subscription.CurrentPeriodEnd);
        Assert.Equal(Now, subscription.CreatedAt);
        Assert.Equal(Now, subscription.UpdatedAt);
    }

    [Fact]
    public void ChangePlan_UpdatesPlanIdAndTimestamp()
    {
        var subscription = Subscription.Create(
            Guid.NewGuid(), Guid.NewGuid(), PaymentProvider.Stripe, "cus_123", "sub_123", SubscriptionStatus.Active, null, Now);
        var newPlanId = Guid.NewGuid();
        var later = Now.AddDays(1);

        subscription.ChangePlan(newPlanId, later);

        Assert.Equal(newPlanId, subscription.PlanId);
        Assert.Equal(later, subscription.UpdatedAt);
    }

    [Fact]
    public void ApplyProviderState_WithANewSubscriptionId_UpdatesIt()
    {
        var subscription = Subscription.Create(
            Guid.NewGuid(), Guid.NewGuid(), PaymentProvider.Wompi, "cus_123", null, SubscriptionStatus.Trialing, null, Now);
        var later = Now.AddDays(1);
        var periodEnd = later.AddMonths(1);

        subscription.ApplyProviderState("src_456", SubscriptionStatus.Active, periodEnd, later);

        Assert.Equal("src_456", subscription.ProviderSubscriptionId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(periodEnd, subscription.CurrentPeriodEnd);
        Assert.Equal(later, subscription.UpdatedAt);
    }

    [Fact]
    public void ApplyProviderState_WithoutASubscriptionId_LeavesTheExistingOneAlone()
    {
        var subscription = Subscription.Create(
            Guid.NewGuid(), Guid.NewGuid(), PaymentProvider.Stripe, "cus_123", "sub_123", SubscriptionStatus.Active, null, Now);

        subscription.ApplyProviderState(null, SubscriptionStatus.PastDue, null, Now.AddDays(1));

        Assert.Equal("sub_123", subscription.ProviderSubscriptionId);
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
    }

    [Fact]
    public void Cancel_SetsStatusToCanceled()
    {
        var subscription = Subscription.Create(
            Guid.NewGuid(), Guid.NewGuid(), PaymentProvider.Stripe, "cus_123", "sub_123", SubscriptionStatus.Active, null, Now);

        subscription.Cancel(Now.AddDays(1));

        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
    }
}
