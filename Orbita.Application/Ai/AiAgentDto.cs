using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// An assistant as ORB-C10's screens see it.
///
/// Deliberately missing: <c>systemPrompt</c>, <c>model</c>, <c>maxTokens</c> and
/// <c>temperature</c>. The configuration screen is forbidden from showing model jargon,
/// so exposing those would only hand the frontend values it is not allowed to display —
/// and would let a UI change what should be a backend decision. <see cref="Tone"/> is the
/// product-facing stand-in for temperature; the model and token budget are resolved per
/// task (ORB-C13).
/// </summary>
/// <param name="Tone">Serialized as its name (<c>"Balanced"</c>), like every enum on this API.</param>
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
    AgentTone Tone,
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
            agent.Tone,
            agent.Tools,
            agent.IsEnabled,
            conversationCount,
            agent.CreatedAt);
}
