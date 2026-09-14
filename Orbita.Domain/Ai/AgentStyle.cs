namespace Orbita.Domain.Ai;

/// <summary>
/// How an assistant speaks, along the three axes ORB-C10's configuration screen shows as
/// sliders.
///
/// Three named levels per axis instead of numbers, for the same reason the single
/// <c>tone</c> it replaces used them: the screen is forbidden from showing model jargon,
/// and the mapping from a named level to a sampling parameter is a model decision that
/// must be tunable without a frontend release. <see cref="AiAgent"/> derives both
/// <c>temperature</c> and <c>max_tokens</c> from this.
/// </summary>
/// <param name="Formality">Replaces the old single-axis <c>tone</c>.</param>
public sealed record AgentStyle(
    FormalityLevel Formality,
    VerbosityLevel Verbosity,
    EnergyLevel Energy)
{
    /// <summary>What a newly created assistant gets, and the middle of every slider.</summary>
    public static AgentStyle Default { get; } =
        new(FormalityLevel.Balanced, VerbosityLevel.Balanced, EnergyLevel.Balanced);
}

/// <summary>Formal ↔ Cercano.</summary>
public enum FormalityLevel
{
    /// <summary>Usted, sin coloquialismos. For regulated or price-sensitive answers.</summary>
    Formal,

    Balanced,

    /// <summary>Tú, cercano. Reads as a person rather than a policy.</summary>
    Warm,
}

/// <summary>Breve ↔ Detallado. The only axis that moves the token budget.</summary>
public enum VerbosityLevel
{
    /// <summary>One or two sentences. What WhatsApp actually rewards.</summary>
    Brief,

    Balanced,

    /// <summary>Explains and gives context. Costs more per reply, so it is opt-in.</summary>
    Detailed,
}

/// <summary>Neutro ↔ Entusiasta.</summary>
public enum EnergyLevel
{
    Neutral,

    Balanced,

    /// <summary>Warmer, more exclamation. Trades some predictability for it.</summary>
    Enthusiastic,
}
