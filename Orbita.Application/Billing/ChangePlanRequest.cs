using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Billing;

public sealed record ChangePlanRequest([Required] Guid PlanId);
