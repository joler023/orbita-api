using Moq;
using Orbita.Application.Billing;
using Orbita.Application.Identity;
using Orbita.Domain.Billing;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class SubscriptionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IPlanRepository> _plans = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<IPaymentProvider> _stripe = new();
    private readonly Mock<IPaymentProvider> _wompi = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly SubscriptionService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    public SubscriptionServiceTests()
    {
        _stripe.Setup(p => p.Kind).Returns(PaymentProvider.Stripe);
        _wompi.Setup(p => p.Kind).Returns(PaymentProvider.Wompi);

        _sut = new SubscriptionService(
            _plans.Object,
            _subscriptions.Object,
            _tenants.Object,
            _users.Object,
            _authorization.Object,
            [_stripe.Object, _wompi.Object],
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    private static Plan NewPlan() => Plan.Create("starter", "Starter", 500, 1000, 29m, "usd", Now);

    [Fact]
    public async Task ListPlansAsync_ReturnsAllActivePlans()
    {
        var plan = NewPlan();
        _plans.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([plan]);

        var result = await _sut.ListPlansAsync(CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal(plan.Code, dto.Code);
    }

    [Fact]
    public async Task SubscribeAsync_ForANonColombianTenant_UsesStripe()
    {
        var tenant = Tenant.Create("acme", "Acme", countryCode: "US", now: Now);
        var owner = User.Create("owner@acme.com", "hash", "Owner Person", Now);
        var plan = NewPlan();
        _tenants.Setup(r => r.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _users.Setup(r => r.GetByIdAsync(_callerId, It.IsAny<CancellationToken>())).ReturnsAsync(owner);
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        _stripe.Setup(p => p.CreateCustomerAsync(owner.Email, owner.FullName, "tok_visa", It.IsAny<CancellationToken>())).ReturnsAsync("cus_stripe");
        _stripe
            .Setup(p => p.CreateSubscriptionAsync("cus_stripe", plan, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderSubscriptionState("sub_stripe", SubscriptionStatus.Active, Now.AddMonths(1)));

        var result = await _sut.SubscribeAsync(_tenantId, _callerId, new SubscribeRequest(plan.Id, "tok_visa"), CancellationToken.None);

        Assert.Equal(PaymentProvider.Stripe, result.Provider);
        _wompi.Verify(p => p.CreateCustomerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _subscriptions.Verify(
            r => r.AddAsync(It.Is<Subscription>(s => s.TenantId == _tenantId && s.ProviderSubscriptionId == "sub_stripe"), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubscribeAsync_ForAColombianTenant_UsesWompi()
    {
        var tenant = Tenant.Create("acme-co", "Acme Colombia", countryCode: "CO", now: Now);
        var owner = User.Create("owner@acme.co", "hash", "Owner Person", Now);
        var plan = NewPlan();
        _tenants.Setup(r => r.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _users.Setup(r => r.GetByIdAsync(_callerId, It.IsAny<CancellationToken>())).ReturnsAsync(owner);
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        _wompi.Setup(p => p.CreateCustomerAsync(owner.Email, owner.FullName, "src_123", It.IsAny<CancellationToken>())).ReturnsAsync("owner@acme.co");
        _wompi
            .Setup(p => p.CreateSubscriptionAsync("owner@acme.co", plan, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderSubscriptionState("src_123", SubscriptionStatus.Active, Now.AddMonths(1)));

        var result = await _sut.SubscribeAsync(_tenantId, _callerId, new SubscribeRequest(plan.Id, "src_123"), CancellationToken.None);

        Assert.Equal(PaymentProvider.Wompi, result.Provider);
        _stripe.Verify(p => p.CreateCustomerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubscribeAsync_WhenATenantAlreadyHasASubscription_ThrowsConflict()
    {
        var existing = Subscription.Create(_tenantId, Guid.NewGuid(), PaymentProvider.Stripe, "cus_1", "sub_1", SubscriptionStatus.Active, null, Now);
        _subscriptions.Setup(r => r.GetByTenantIdAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        await Assert.ThrowsAsync<SubscriptionAlreadyExistsException>(
            () => _sut.SubscribeAsync(_tenantId, _callerId, new SubscribeRequest(Guid.NewGuid(), "tok"), CancellationToken.None));
    }

    [Fact]
    public async Task SubscribeAsync_WithAnUnknownPlan_ThrowsPlanNotFound()
    {
        _plans.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Plan?)null);

        await Assert.ThrowsAsync<PlanNotFoundException>(
            () => _sut.SubscribeAsync(_tenantId, _callerId, new SubscribeRequest(Guid.NewGuid(), "tok"), CancellationToken.None));
    }

    [Fact]
    public async Task SubscribeAsync_WhenCallerLacksPermission_PropagatesForbiddenWithoutCreatingAnything()
    {
        _authorization
            .Setup(a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ManageBilling, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("nope"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.SubscribeAsync(_tenantId, _callerId, new SubscribeRequest(Guid.NewGuid(), "tok"), CancellationToken.None));

        _subscriptions.Verify(r => r.AddAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePlanAsync_UpdatesTheLocalSubscriptionFromTheProviderResponse()
    {
        var oldPlanId = Guid.NewGuid();
        var newPlan = NewPlan();
        var subscription = Subscription.Create(_tenantId, oldPlanId, PaymentProvider.Stripe, "cus_1", "sub_1", SubscriptionStatus.Active, Now, Now);
        _subscriptions.Setup(r => r.GetByTenantIdAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(subscription);
        _plans.Setup(r => r.GetByIdAsync(newPlan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(newPlan);
        _stripe
            .Setup(p => p.ChangeSubscriptionAsync("sub_1", newPlan, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderSubscriptionState("sub_1", SubscriptionStatus.Active, Now.AddMonths(2)));

        var result = await _sut.ChangePlanAsync(_tenantId, _callerId, new ChangePlanRequest(newPlan.Id), CancellationToken.None);

        Assert.Equal(newPlan.Id, result.PlanId);
        Assert.Equal(Now.AddMonths(2), subscription.CurrentPeriodEnd);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePlanAsync_WithNoExistingSubscription_ThrowsNotFound()
    {
        _subscriptions.Setup(r => r.GetByTenantIdAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync((Subscription?)null);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => _sut.ChangePlanAsync(_tenantId, _callerId, new ChangePlanRequest(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task CancelAsync_CallsTheProviderAndMarksTheSubscriptionCanceled()
    {
        var subscription = Subscription.Create(_tenantId, Guid.NewGuid(), PaymentProvider.Stripe, "cus_1", "sub_1", SubscriptionStatus.Active, Now, Now);
        _subscriptions.Setup(r => r.GetByTenantIdAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(subscription);

        await _sut.CancelAsync(_tenantId, _callerId, CancellationToken.None);

        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
        _stripe.Verify(p => p.CancelSubscriptionAsync("sub_1", It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListInvoicesAsync_DelegatesToTheSubscriptionsProvider()
    {
        var subscription = Subscription.Create(_tenantId, Guid.NewGuid(), PaymentProvider.Wompi, "cus_1", "src_1", SubscriptionStatus.Active, Now, Now);
        _subscriptions.Setup(r => r.GetByTenantIdAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(subscription);
        var invoices = new List<InvoiceSummary> { new("inv_1", Now, 29m, "COP", "paid", "https://example.com/inv_1") };
        _wompi.Setup(p => p.ListInvoicesAsync("cus_1", It.IsAny<CancellationToken>())).ReturnsAsync(invoices);

        var result = await _sut.ListInvoicesAsync(_tenantId, _callerId, CancellationToken.None);

        Assert.Same(invoices, result);
    }
}
