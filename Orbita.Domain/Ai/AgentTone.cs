namespace Orbita.Domain.Ai;

/// <summary>
/// How chatty an assistant is allowed to be — ORB-C10's "¿qué tan creativo quieres que
/// sea?".
///
/// This exists instead of exposing <c>temperature</c> because the product surface is
/// forbidden from showing model jargon, and because the mapping from a named level to a
/// number is a model decision, not a presentation one: tuning what "Balanced" means must
/// never require a frontend release. <see cref="AiAgent"/> writes the corresponding
/// temperature whenever the tone changes, so the column orbita-schema.dbml specifies
/// still holds a real value for whatever reads it.
/// </summary>
public enum AgentTone
{
    /// <summary>Sticks closely to the instructions and the documents. For regulated or price-sensitive answers.</summary>
    Formal,

    /// <summary>The default: natural wording, but it does not stray from what it was told.</summary>
    Balanced,

    /// <summary>Warmer and more talkative. Costs some predictability.</summary>
    Conversational,
}
