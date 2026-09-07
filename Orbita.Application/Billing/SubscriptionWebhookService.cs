using Orbita.Domain.Billing;
using Orbita.Domain.Common;

namespace Orbita.Application.Billing;

public sealed class SubscriptionWebhookService(
    IEnumerable<IPaymentProvider> paymentProviders,
    ISubscriptionRepository subscriptionRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ISubscriptionWebhookService
{
    public async Task HandleWebhookAsync(PaymentProvider provider, string payload, string signatureHeader, CancellationToken cancellationToken)
    {
        var paymentProvider = paymentProviders.SingleOrDefault(p => p.Kind == provider)
            ?? throw new InvalidOperationException($"No IPaymentProvider registered for '{provider}'.");

        var state = paymentProvider.ParseWebhookEvent(payload, signatureHeader);
        if (state is null)
        {
            return;
        }

        var subscription = await subscriptionRepository.GetByProviderSubscriptionIdAsync(provider, state.ProviderSubscriptionId, cancellationToken);
        if (subscription is null)
        {
            // Nothing local to update — most likely a test event fired from the
            // provider's dashboard before any real subscription used this id.
            return;
        }

        subscription.ApplyProviderState(state.ProviderSubscriptionId, state.Status, state.CurrentPeriodEnd, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
