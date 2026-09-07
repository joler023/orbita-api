using Orbita.Domain.Billing;

namespace Orbita.Application.Billing;

public sealed record ProviderSubscriptionState(string ProviderSubscriptionId, SubscriptionStatus Status, DateTimeOffset? CurrentPeriodEnd);
