using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// An assistant as ORB-C10's screens see it: the <b>live</b> configuration, plus whatever
/// is pending publication.
///
/// Deliberately missing: <c>systemPrompt</c>, <c>model</c>, <c>maxTokens</c> and
/// <c>temperature</c>. The configuration screen is forbidden from showing model jargon, so
/// exposing those would only hand the frontend values it is not allowed to display — and
/// would let a UI change what should be a backend decision. <see cref="AgentStyleDto"/> is
/// the product-facing stand-in; the model and token budget are resolved per task (ORB-C13)
/// and per verbosity.
/// </summary>
/// <param name="Draft">
/// What the form should bind to when present — the owner's unpublished edits. Null when
/// there is nothing pending, in which case the form binds to the live fields above. Both
/// are returned so the screen can show "this is what customers get" next to "this is what
/// you changed".
/// </param>
/// <param name="HasUnpublishedChanges">
/// True exactly when <paramref name="Draft"/> is present and genuinely differs from live.
/// A save that changed nothing does not raise it.
/// </param>
/// <param name="ConversationCount">
/// Conversations handled in the last 30 days — what screen 2.5 shows next to the switch.
/// Always <c>0</c> until ORB-C04 exists, because it is counted from
/// <c>ai_runs.conversation_id</c> and there are no conversations yet. The field ships now
/// so the shape does not change under the frontend later.
/// </param>
public sealed record AiAgentDto(
    Guid Id,
    string Name,
    string Personality,
    string Instructions,
    AgentStyleDto Style,
    IReadOnlyList<string> Tools,
    bool IsEnabled,
    bool HasUnpublishedChanges,
    AiAgentDraftDto? Draft,
    int ConversationCount,
    DateTimeOffset CreatedAt,
    AgentGuardrailsDto Guardrails)
{
    public static AiAgentDto From(AiAgent agent, AiAgentDraft? draft = null, int conversationCount = 0)
    {
        // A draft that matches live is not a pending change; it just has not been cleaned
        // up yet. Reporting it would light the badge for nothing, and a badge that lies is
        // a badge people learn to ignore.
        var pending = draft is not null && draft.DiffersFrom(agent) ? draft : null;

        return new AiAgentDto(
            agent.Id,
            agent.Name,
            agent.Personality,
            agent.Instructions,
            AgentStyleDto.From(agent.Style),
            agent.Tools,
            agent.IsEnabled,
            pending is not null,
            pending is null ? null : AiAgentDraftDto.From(pending),
            conversationCount,
            agent.CreatedAt,
            new AgentGuardrailsDto(agent.BlockedTopics, agent.OutOfScopeReply));
    }
}

/// <summary>
/// ORB-C06's "de qué prefieres que no hable" section. Never part of the draft: it takes
/// effect the moment it is saved, like <c>PATCH .../enabled</c>. A setting whose purpose
/// is to make the assistant stop talking about something cannot wait for someone to
/// press Publicar — the screen saying "guardado" while the risk keeps running would be
/// the feature working backwards.
/// </summary>
public sealed record AgentGuardrailsDto(IReadOnlyList<string> BlockedTopics, string OutOfScopeReply);

/// <summary>The three sliders on ORB-C10's "Quién es" card.</summary>
public sealed record AgentStyleDto(
    FormalityLevel Formality,
    VerbosityLevel Verbosity,
    EnergyLevel Energy)
{
    public static AgentStyleDto From(AgentStyle style)
        => new(style.Formality, style.Verbosity, style.Energy);

    public AgentStyle ToStyle() => new(Formality, Verbosity, Energy);
}

/// <param name="UpdatedAt">When the draft was last saved, for copy like "guardado hace 5 min".</param>
public sealed record AiAgentDraftDto(
    string Name,
    string Personality,
    string Instructions,
    AgentStyleDto Style,
    IReadOnlyList<string> Tools,
    DateTimeOffset UpdatedAt)
{
    public static AiAgentDraftDto From(AiAgentDraft draft)
        => new(
            draft.Name,
            draft.Personality,
            draft.Instructions,
            AgentStyleDto.From(draft.Style),
            draft.Tools,
            draft.UpdatedAt);
}
