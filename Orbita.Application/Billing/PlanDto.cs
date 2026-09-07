namespace Orbita.Application.Billing;

public sealed record PlanDto(
    Guid Id,
    string Code,
    string Name,
    int IncludedConversations,
    int IncludedAiCredits,
    decimal PriceAmount,
    string PriceCurrency);
