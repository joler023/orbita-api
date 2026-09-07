using Orbita.Domain.Billing;

namespace Orbita.Application.Billing;

public sealed record SubscriptionDto(
    Guid Id,
    Guid PlanId,
    string PlanCode,
    string PlanName,
    PaymentProvider Provider,
    SubscriptionStatus Status,
    DateTimeOffset? CurrentPeriodEnd);
