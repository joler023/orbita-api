using Orbita.Domain.Common;

namespace Orbita.Domain.Billing;

/// <summary>
/// A subscribable tier (ORB-A12): conversations included, AI credits included, and a
/// price. Global reference data, not tenant-scoped — every tenant sees the same
/// catalog. Not in orbita-schema.dbml, which predates billing.
/// </summary>
public sealed class Plan : Entity
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 100;

    private Plan(
        Guid id,
        string code,
        string name,
        int includedConversations,
        int includedAiCredits,
        decimal priceAmount,
        string priceCurrency,
        string? stripePriceId,
        bool isActive,
        DateTimeOffset createdAt)
        : base(id)
    {
        Code = code;
        Name = name;
        IncludedConversations = includedConversations;
        IncludedAiCredits = includedAiCredits;
        PriceAmount = priceAmount;
        PriceCurrency = priceCurrency;
        StripePriceId = stripePriceId;
        IsActive = isActive;
        CreatedAt = createdAt;
    }

    /// <summary>Stable identifier used in code/config (e.g. "starter") — never shown to a customer.</summary>
    public string Code { get; }

    public string Name { get; private set; }

    public int IncludedConversations { get; private set; }

    public int IncludedAiCredits { get; private set; }

    public decimal PriceAmount { get; private set; }

    /// <summary>ISO 4217, e.g. "USD" or "COP".</summary>
    public string PriceCurrency { get; private set; }

    /// <summary>
    /// Stripe's Price id for this plan — required before StripePaymentProvider can
    /// create or change a subscription against it. Null until someone creates the
    /// matching Price in the Stripe dashboard/API and records its id here. Wompi has
    /// no equivalent: it has no native subscription/price object (see
    /// WompiPaymentProvider's class comment), so nothing analogous exists for it.
    /// </summary>
    public string? StripePriceId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static Plan Create(
        string code,
        string name,
        int includedConversations,
        int includedAiCredits,
        decimal priceAmount,
        string priceCurrency,
        DateTimeOffset now)
        => new(
            Guid.NewGuid(),
            RequireLength(code, nameof(code), 1, CodeMaxLength),
            RequireLength(name, nameof(name), 1, NameMaxLength),
            includedConversations,
            includedAiCredits,
            priceAmount,
            RequireLength(priceCurrency, nameof(priceCurrency), 3, 3).ToUpperInvariant(),
            stripePriceId: null,
            isActive: true,
            createdAt: now);

    public void SetStripePriceId(string stripePriceId)
        => StripePriceId = RequireLength(stripePriceId, nameof(stripePriceId), 1, 200);

    public void Deactivate() => IsActive = false;

    private static string RequireLength(string value, string paramName, int minLength, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            throw new ArgumentException(
                $"{paramName} must be between {minLength} and {maxLength} characters.",
                paramName);
        }

        return trimmed;
    }
}
