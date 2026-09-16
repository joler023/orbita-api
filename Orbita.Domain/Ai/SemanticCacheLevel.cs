namespace Orbita.Domain.Ai;

/// <summary>
/// How freely an assistant may reuse an answer it already gave (ORB-C12), as a product
/// decision instead of a number.
///
/// The same call ORB-C10 made for <c>temperature</c>: a cosine-similarity threshold is not
/// something the owner of a bakery can reason about — "is 0,92 a lot?" has no answer — and
/// the cost of guessing wrong lands on a customer, who gets the answer to a question they
/// did not ask. So the owner picks how careful to be and the numbers stay here, where they
/// can be tuned without anyone changing a screen.
/// </summary>
public enum SemanticCacheLevel
{
    /// <summary>The default: every answer is generated for the customer who asked.</summary>
    Off = 0,

    /// <summary>Reuses only near-identical questions.</summary>
    Conservative = 1,

    /// <summary>Reuses questions that clearly ask the same thing.</summary>
    Balanced = 2,

    /// <summary>Reuses more, and saves more, at the cost of the occasional loose match.</summary>
    Aggressive = 3,
}

/// <summary>The one place a level becomes a similarity threshold, and back.</summary>
public static class SemanticCacheLevels
{
    public const decimal ConservativeThreshold = 0.97m;

    public const decimal BalancedThreshold = 0.93m;

    public const decimal AggressiveThreshold = 0.88m;

    public static decimal? ThresholdFor(SemanticCacheLevel level) => level switch
    {
        SemanticCacheLevel.Conservative => ConservativeThreshold,
        SemanticCacheLevel.Balanced => BalancedThreshold,
        SemanticCacheLevel.Aggressive => AggressiveThreshold,
        _ => null,
    };

    /// <summary>
    /// The level a stored threshold came from. Nearest match rather than exact, so retuning
    /// a level's number never turns every assistant already on it into something unreadable.
    /// </summary>
    public static SemanticCacheLevel From(decimal? threshold)
    {
        if (threshold is not { } value)
        {
            return SemanticCacheLevel.Off;
        }

        SemanticCacheLevel[] levels = [SemanticCacheLevel.Conservative, SemanticCacheLevel.Balanced, SemanticCacheLevel.Aggressive];

        return levels.MinBy(level => Math.Abs(ThresholdFor(level)!.Value - value));
    }
}
