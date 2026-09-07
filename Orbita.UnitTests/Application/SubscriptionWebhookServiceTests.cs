using Moq;
using Orbita.Application.Billing;
using Orbita.Domain.Billing;
using Orbita.Domain.Common;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class SubscriptionWebhookServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IPaymentProvider> _stripe = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly SubscriptionWebhookService _sut;

    public SubscriptionWebhookServiceTests()
    {
        _stripe.Setup(p => p.Kind).Returns(PaymentProvider.Stripe);
        _sut = new SubscriptionWebhookService([_stripe.Object], _subscriptions.Object, _unitOfWork.Object, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task HandleWebhookAsync_ForAKnownSubscription_UpdatesItsStatus()
    {
        var subscription = Subscription.Create(Guid.NewGuid(), Guid.NewGuid(), PaymentProvider.Stripe, "cus_1", "sub_1", SubscriptionStatus.Active, Now, Now);
        _stripe.Setup(p => p.ParseWebhookEvent("payload", "sig")).Returns(new ProviderSubscriptionState("sub_1", SubscriptionStatus.PastDue, Now.AddDays(3)));
        _subscriptions
            .Setup(r => r.GetByProviderSubscriptionIdAsync(PaymentProvider.Stripe, "sub_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        await _sut.HandleWebhookAsync(PaymentProvider.Stripe, "payload", "sig", CancellationToken.None);

        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(Now.AddDays(3), subscription.CurrentPeriodEnd);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_ForAnUnknownSubscription_DoesNothing()
    {
        _stripe.Setup(p => p.ParseWebhookEvent("payload", "sig")).Returns(new ProviderSubscriptionState("sub_unknown", SubscriptionStatus.Active, null));
        _subscriptions
            .Setup(r => r.GetByProviderSubscriptionIdAsync(PaymentProvider.Stripe, "sub_unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subscription?)null);

        await _sut.HandleWebhookAsync(PaymentProvider.Stripe, "payload", "sig", CancellationToken.None);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleWebhookAsync_WhenTheEventIsNotAboutASubscription_DoesNothing()
    {
        _stripe.Setup(p => p.ParseWebhookEvent("payload", "sig")).Returns((ProviderSubscriptionState?)null);

        await _sut.HandleWebhookAsync(PaymentProvider.Stripe, "payload", "sig", CancellationToken.None);

        _subscriptions.Verify(r => r.GetByProviderSubscriptionIdAsync(It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleWebhookAsync_WhenSignatureVerificationFails_PropagatesTheException()
    {
        _stripe.Setup(p => p.ParseWebhookEvent(It.IsAny<string>(), It.IsAny<string>())).Throws<InvalidWebhookSignatureException>();

        await Assert.ThrowsAsync<InvalidWebhookSignatureException>(
            () => _sut.HandleWebhookAsync(PaymentProvider.Stripe, "payload", "bad-sig", CancellationToken.None));
    }
}
