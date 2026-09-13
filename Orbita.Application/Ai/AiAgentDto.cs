using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// An assistant as ORB-C10's screens see it.
///
/// Deliberately missing: <c>systemPrompt</c>, <c>model</c>, <c>maxTokens</c> and
/// <c>temperature</c>. The configuration screen is forbidden from showing model jargon,
/// so exposing those would only hand the frontend values it is not allowed to display —
/// and would let a UI change what should be a backend decision. <see cref="AgentStyleDto"/>
/// is the product-facing stand-in; the model is resolved per task (ORB-C13) and the token
/// budget per verbosity.
/// </summary>
/// <param name="Style">The three sliders. Each level serializes as its name (<c>"Balanced"</c>).</param>
/// <param name="Tools">Enabled tool keys, from <see cref="AiToolCatalog"/>.</param>
/// <param name="ConversationCount">
/// How many conversations this assistant handled in the last 30 days — what screen 2.5
/// shows next to the on/off switch. Always <c>0</c> until ORB-C04 exists, because it is
/// counted from <c>ai_runs.conversation_id</c> and there are no conversations yet. The
/// field ships now so the shape does not change under the frontend later.
/// </param>
public sealed record AiAgentDto(
    Guid Id,
    string Name,
    string Personality,
    string Instructions,
    AgentStyleDto Style,
    IReadOnlyList<string> Tools,
    bool IsEnabled,
    int ConversationCount,
    DateTimeOffset CreatedAt)
{
    public static AiAgentDto From(AiAgent agent, int conversationCount = 0)
        => new(
            agent.Id,
            agent.Name,
            agent.Personality,
            agent.Instructions,
            AgentStyleDto.From(agent.Style),
            agent.Tools,
            agent.IsEnabled,
            conversationCount,
            agent.CreatedAt);
}

/// <summary>
/// The three sliders on ORB-C10's "Quién es" card: formal↔cercano, breve↔detallado,
/// neutro↔entusiasta.
/// </summary>
public sealed record AgentStyleDto(
    FormalityLevel Formality,
    VerbosityLevel Verbosity,
    EnergyLevel Energy)
{
    public static AgentStyleDto From(AgentStyle style)
        => new(style.Formality, style.Verbosity, style.Energy);

    public AgentStyle ToStyle() => new(Formality, Verbosity, Energy);
}
