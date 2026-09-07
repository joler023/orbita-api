using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Billing;

public sealed record SubscribeRequest(
    [Required] Guid PlanId,
    [Required] string PaymentMethodToken);
