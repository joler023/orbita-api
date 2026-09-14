using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// Pending, unpublished changes to an assistant's configuration.
///
/// <para><b>Why this exists.</b> The UX document is explicit: "nadie debería editar en
/// caliente un agente que está atendiendo clientes". Without a draft, every keystroke
/// saved on ORB-C10's screen would change what live customers are being told — including
/// half-finished instructions. So editing writes here, and only publishing copies it onto
/// <see cref="AiAgent"/>.</para>
///
/// <para><b>Why a separate row rather than columns on the agent.</b> Doubling every
/// editable column with a <c>draft_</c> twin makes the table twice as wide and every query
/// twice as easy to get wrong; and "is there a draft?" becomes a comparison instead of a
/// row existing. Here the answer is simply whether this row is present.</para>
///
/// The primary key is the agent's own id: an assistant has at most one pending draft, so
/// saving twice replaces rather than accumulates.
/// </summary>
public sealed class AiAgentDraft : Entity
{
    private readonly List<string> _tools = [];

    // The parameter is named `id`, not `agentId`, because EF Core binds constructor
    // parameters to mapped properties by name and the mapped property is Entity.Id.
    private AiAgentDraft(
        Guid id,
        Guid tenantId,
        string name,
        string personality,
        string instructions,
        AgentStyle style,
        DateTimeOffset updatedAt)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Personality = personality;
        Instructions = instructions;
        Style = style;
        UpdatedAt = updatedAt;
    }

    /// <summary>EF Core materialization only — see the same constructor on <see cref="AiAgent"/>.</summary>
    private AiAgentDraft()
        : base(Guid.Empty)
    {
        // All four columns are NOT NULL and EF fills them in right after constructing.
        Name = null!;
        Personality = null!;
        Instructions = null!;
        Style = null!;
    }

    public Guid TenantId { get; }

    /// <summary>The assistant this draft belongs to — the draft's <see cref="Entity.Id"/> is that assistant's id.</summary>
    public Guid AgentId => Id;

    public string Name { get; private set; }

    public string Personality { get; private set; }

    public string Instructions { get; private set; }

    public AgentStyle Style { get; private set; }

    public IReadOnlyList<string> Tools => _tools;

    public DateTimeOffset UpdatedAt { get; private set; }

    public static AiAgentDraft For(
        AiAgent agent,
        string name,
        string personality,
        string instructions,
        AgentStyle style,
        IReadOnlyList<string> tools,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var draft = new AiAgentDraft(agent.Id, agent.TenantId, name, personality, instructions, style, now);
        draft.Replace(name, personality, instructions, style, tools, now);

        return draft;
    }

    /// <summary>Saving again replaces the pending draft rather than stacking another one.</summary>
    public void Replace(
        string name,
        string personality,
        string instructions,
        AgentStyle style,
        IReadOnlyList<string> tools,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(tools);

        Name = name.Trim();
        Personality = personality.Trim();
        Instructions = instructions.Trim();
        Style = style;
        UpdatedAt = now;

        _tools.Clear();
        _tools.AddRange(tools.Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// The assistant this draft <em>would</em> be if it were published — built, never stored.
    ///
    /// ORB-C11's test bench runs against this: trying changes out before customers see them
    /// is the entire point of having a draft, so a test chat that answered with the
    /// published configuration would be answering the wrong question.
    ///
    /// Its <see cref="Entity.Id"/> is a fresh one and means nothing. Anything attributing
    /// work to the real assistant — an <c>ai_runs</c> row, say — must use the id of the
    /// agent this draft belongs to, never the preview's.
    /// </summary>
    public AiAgent AsUnsavedPreview(DateTimeOffset now)
    {
        var preview = AiAgent.Create(TenantId, Name, Personality, Instructions, Style, now);
        preview.EnableTools(_tools);

        return preview;
    }

    /// <summary>
    /// Whether this draft actually differs from what is live.
    ///
    /// Saving a form unchanged must not leave the screen claiming there are unpublished
    /// changes — that badge is the only signal the owner has, so a false positive teaches
    /// them to ignore it.
    /// </summary>
    public bool DiffersFrom(AiAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return !string.Equals(Name, agent.Name, StringComparison.Ordinal)
            || !string.Equals(Personality, agent.Personality, StringComparison.Ordinal)
            || !string.Equals(Instructions, agent.Instructions, StringComparison.Ordinal)
            || Style != agent.Style
            || !_tools.OrderBy(tool => tool, StringComparer.Ordinal)
                .SequenceEqual(agent.Tools.OrderBy(tool => tool, StringComparer.Ordinal), StringComparer.Ordinal);
    }
}
