using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// A configurable AI assistant belonging to one tenant (orbita-schema.dbml's
/// <c>ai_agents</c>), and the thing ORB-C10's configuration screen edits.
///
/// <para><b>Two vocabularies, one row.</b> The owner chooses a <see cref="Personality"/>,
/// some <see cref="Instructions"/> and a <see cref="Style"/>. The model needs a
/// <see cref="SystemPrompt"/>, a <see cref="Temperature"/> and a token budget. This entity
/// holds both and keeps the second derived from the first, so neither side has to know
/// about the other: the screen never shows the words "prompt", "temperature" or "tokens"
/// (a hard product rule — ORB-C10 is the second hardest screen in the product precisely
/// because it asks a non-technical person to configure an AI), and ORB-C04 can read
/// <c>SystemPrompt</c> without knowing how it was assembled.</para>
///
/// <para>Keeping the derived values in columns rather than computing them at read time is
/// deliberate: orbita-schema.dbml specifies <c>system_prompt</c>, <c>temperature</c> and
/// <c>max_tokens</c> as real columns, and anything reading the table directly — a worker, a
/// migration, an analyst — should find a usable value there.</para>
///
/// <para><b>Editing does not happen here.</b> Once an assistant exists, ORB-C10's screen
/// writes an <see cref="AiAgentDraft"/>; only publishing copies it onto this row. Nobody
/// edits in place an assistant that is answering customers.</para>
///
/// <see cref="IsEnabled"/> defaults to false: an assistant that starts answering customers
/// the moment an organization is created would be a nasty surprise.
/// </summary>
public sealed class AiAgent : Entity
{
    private readonly List<string> _tools = [];

    private readonly List<string> _blockedTopics = [];

    private AiAgent(
        Guid id,
        Guid tenantId,
        string name,
        string personality,
        string instructions,
        AgentStyle style,
        bool isEnabled,
        DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Personality = personality;
        Instructions = instructions;
        Style = style;
        Temperature = TemperatureFor(style);
        MaxTokens = MaxTokensFor(style);
        IsEnabled = isEnabled;
        CreatedAt = createdAt;
        SystemPrompt = ComposePrompt(name, personality, instructions, style);
    }

    /// <summary>
    /// EF Core materialization only. <see cref="Style"/> is mapped as a complex property
    /// across three columns, and EF cannot bind one of those to a constructor parameter —
    /// so the constructor above, the one that also derives the prompt and the sampling
    /// values, is not the one it can call. It writes every mapped member directly instead.
    /// </summary>
    private AiAgent()
        : base(Guid.Empty)
    {
        // EF assigns all of these from NOT NULL columns immediately after constructing the
        // instance, so no null ever reaches anything that reads them.
        Name = null!;
        Personality = null!;
        Instructions = null!;
        Style = null!;
        SystemPrompt = null!;
    }

    public Guid TenantId { get; }

    public string Name { get; private set; }

    /// <summary>How the assistant should come across. Free text written by the owner.</summary>
    public string Personality { get; private set; }

    /// <summary>What it should and should not do. Free text written by the owner.</summary>
    public string Instructions { get; private set; }

    /// <summary>The three sliders: formal↔warm, brief↔detailed, neutral↔enthusiastic.</summary>
    public AgentStyle Style { get; private set; }

    /// <summary>
    /// The standing instructions actually sent to the model, composed from
    /// <see cref="Personality"/>, <see cref="Instructions"/> and <see cref="Style"/>.
    /// Never edited directly and never shown to a user.
    /// </summary>
    public string SystemPrompt { get; private set; }

    /// <summary>
    /// Derived from <see cref="Style"/> and rewritten whenever it changes. Stored rather
    /// than computed so anything reading <c>ai_agents</c> directly finds a real value in
    /// the column orbita-schema.dbml specifies.
    /// </summary>
    public decimal Temperature { get; private set; }

    /// <summary>Derived from the style's verbosity — the only axis that costs money per reply.</summary>
    public int MaxTokens { get; private set; }

    /// <summary>
    /// Keys from <see cref="AiToolCatalog"/> that this assistant may call. Only tools that
    /// actually work can be here — see <see cref="EnableTools"/>.
    /// </summary>
    public IReadOnlyList<string> Tools => _tools;

    /// <summary>
    /// Subjects this assistant must not handle, in the owner's own words (ORB-C06).
    ///
    /// Free text rather than a catalog, because what is out of scope is entirely the
    /// business's call: a clinic blocks "diagnóstico" and "dosis", a bakery blocks
    /// "descuento". A closed list drawn up by us would be wrong for almost everyone.
    ///
    /// Matching is a case- and accent-insensitive substring test on the customer's
    /// message, checked before the model is called — so an out-of-scope question costs
    /// nothing and gets a person instead of an answer.
    /// </summary>
    public IReadOnlyList<string> BlockedTopics => _blockedTopics;

    /// <summary>
    /// What the customer hears when they ask about a blocked subject (ORB-C06).
    ///
    /// Editable by the business, with a default, rather than a sentence the backend picks.
    /// It never passes through the model — that is the point of blocking before the call —
    /// so it cannot mirror the customer's language or treatment the way a generated reply
    /// does. Given that, the owner's own words are worth more than ours: she knows how she
    /// wants to sound when her assistant declines.
    ///
    /// The default deliberately promises nothing the product cannot do. "Ya les aviso y te
    /// escriben" would be a lie today: ORB-C07 does not exist, there is no human queue and
    /// nobody gets notified. When C07 lands, the default can promise the handoff.
    /// </summary>
    public string OutOfScopeReply { get; private set; } = DefaultOutOfScopeReply;

    /// <summary>
    /// When this assistant is on duty (ORB-C08). Null means always on. Like guardrails,
    /// not part of the draft: "stop answering after 6pm" has to hold the moment it is saved.
    /// </summary>
    public BusinessHours? BusinessHours { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Default from orbita-schema.dbml's <c>ai_agents</c> definition.</summary>
    public const int DefaultMaxTokens = 800;

    public const int NameMaxLength = 120;

    public const int BlockedTopicMaxLength = 120;

    public const int MaxBlockedTopics = 50;

    public const int OutOfScopeReplyMaxLength = 500;

    public const string DefaultOutOfScopeReply =
        "Eso prefiero que te lo responda alguien del equipo. Escríbeles directamente y con gusto te ayudan.";

    public const int PersonalityMaxLength = 2_000;

    public const int InstructionsMaxLength = 8_000;

    /// <summary>
    /// The default assistant seeded when an organization registers, so the owner finds
    /// something to configure rather than an empty screen — the same reasoning ORB-D04
    /// applies to the default pipeline. Disabled, and with only the one tool that works.
    /// </summary>
    public static AiAgent CreateDefault(Guid tenantId, string businessName, DateTimeOffset now)
    {
        var agent = Create(
            tenantId,
            "Asistente",
            $"Eres el asistente virtual de {businessName}.",
            "Respondes en el idioma en que te escriban. Si no sabes algo, lo dices y ofreces "
                + "pasar la conversación a una persona del equipo. Nunca inventas información.",
            AgentStyle.Default,
            now);

        agent.EnableTools([AiToolCatalog.ConsultarConocimiento]);

        return agent;
    }

    public static AiAgent Create(
        Guid tenantId,
        string name,
        string personality,
        string instructions,
        AgentStyle style,
        DateTimeOffset now)
    {
        ValidateText(name, NameMaxLength, nameof(name));
        ValidateText(personality, PersonalityMaxLength, nameof(personality));
        ValidateText(instructions, InstructionsMaxLength, nameof(instructions));
        ArgumentNullException.ThrowIfNull(style);

        return new AiAgent(
            Guid.NewGuid(), tenantId, name.Trim(), personality.Trim(), instructions.Trim(), style, isEnabled: false, now);
    }

    /// <summary>
    /// Overwrites the live configuration. Called when an <see cref="AiAgentDraft"/> is
    /// published, never straight from a request — a request writes a draft.
    /// </summary>
    public void ApplyConfiguration(
        string name,
        string personality,
        string instructions,
        AgentStyle style,
        IReadOnlyList<string> tools)
    {
        ValidateText(name, NameMaxLength, nameof(name));
        ValidateText(personality, PersonalityMaxLength, nameof(personality));
        ValidateText(instructions, InstructionsMaxLength, nameof(instructions));
        ArgumentNullException.ThrowIfNull(style);

        Name = name.Trim();
        Personality = personality.Trim();
        Instructions = instructions.Trim();
        Style = style;
        Temperature = TemperatureFor(style);
        MaxTokens = MaxTokensFor(style);
        SystemPrompt = ComposePrompt(Name, Personality, Instructions, style);

        EnableTools(tools);
    }

    /// <summary>
    /// Replaces the enabled tools wholesale — the screen edits them as a set of
    /// checkboxes, so a partial update would need the caller to know what was there
    /// before.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A key is unknown, or names a tool that does not work yet. Enabling something that
    /// can never run would look configured and silently do nothing.
    /// </exception>
    public void EnableTools(IReadOnlyList<string> toolKeys)
    {
        ArgumentNullException.ThrowIfNull(toolKeys);

        foreach (var key in toolKeys)
        {
            if (!AiToolCatalog.Exists(key))
            {
                throw new ArgumentException($"'{key}' is not a known tool.", nameof(toolKeys));
            }

            if (!AiToolCatalog.IsAvailable(key))
            {
                throw new ArgumentException($"The tool '{key}' is not available yet.", nameof(toolKeys));
            }
        }

        _tools.Clear();
        _tools.AddRange(toolKeys.Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// Replaces the out-of-scope subjects wholesale, same reason <see cref="EnableTools"/>
    /// does: the screen edits them as one list.
    ///
    /// Blank entries are dropped rather than rejected — an empty line left in a textarea
    /// would otherwise match every message ever sent and silence the assistant completely.
    /// </summary>
    public void SetBusinessHours(BusinessHours? businessHours) => BusinessHours = businessHours;

    public void SetGuardrails(IReadOnlyList<string> topics, string outOfScopeReply)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentException.ThrowIfNullOrWhiteSpace(outOfScopeReply);

        if (outOfScopeReply.Length > OutOfScopeReplyMaxLength)
        {
            throw new ArgumentException(
                $"The reply must be at most {OutOfScopeReplyMaxLength} characters.", nameof(outOfScopeReply));
        }

        // Blank entries are dropped rather than rejected. An empty string would match
        // every message ever sent and silence the assistant completely, so the one thing
        // it must never become is a topic.
        var cleaned = topics
            .Select(topic => topic.Trim())
            .Where(topic => topic.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count > MaxBlockedTopics)
        {
            throw new ArgumentException($"At most {MaxBlockedTopics} topics can be blocked.", nameof(topics));
        }

        foreach (var topic in cleaned)
        {
            if (topic.Length > BlockedTopicMaxLength)
            {
                throw new ArgumentException(
                    $"A blocked topic must be at most {BlockedTopicMaxLength} characters.", nameof(topics));
            }
        }

        _blockedTopics.Clear();
        _blockedTopics.AddRange(cleaned);
        OutOfScopeReply = outOfScopeReply.Trim();
    }

    /// <summary>
    /// Turning an assistant on is what puts it in front of customers, so it is its own
    /// operation rather than a field on a bulk update — the list screen toggles it
    /// directly, without loading the rest.
    ///
    /// Independent of publishing: enabling does not publish a pending draft, and
    /// publishing does not enable. Conflating them would mean saving a configuration
    /// accidentally turned an assistant loose on customers.
    /// </summary>
    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    /// <summary>
    /// Assembles what the model actually receives. Kept here, in the domain, because it is
    /// the rule that turns product concepts into one model concept — not a presentation
    /// detail and not a persistence one.
    /// </summary>
    private static string ComposePrompt(string name, string personality, string instructions, AgentStyle style)
        => $"""
            Te llamas {name}.

            {personality}

            {instructions}

            {StyleGuidance(style)}
            """;

    /// <summary>
    /// The three sliders, said in words the model can act on. Sampling parameters alone
    /// cannot express "brief" or "warm" — temperature makes a reply more varied, not
    /// shorter or friendlier — so the style has to reach the model as instructions too.
    /// </summary>
    private static string StyleGuidance(AgentStyle style)
    {
        var formality = style.Formality switch
        {
            FormalityLevel.Formal => "Tratas de usted y evitas coloquialismos.",
            FormalityLevel.Warm => "Tratas de tú, con cercanía.",
            _ => "Usas un trato natural, ni distante ni excesivamente familiar.",
        };

        var verbosity = style.Verbosity switch
        {
            VerbosityLevel.Brief => "Respondes en una o dos frases.",
            VerbosityLevel.Detailed => "Explicas con contexto y ejemplos cuando ayudan.",
            _ => "Respondes con el detalle justo.",
        };

        var energy = style.Energy switch
        {
            EnergyLevel.Neutral => "Mantienes un tono sobrio.",
            EnergyLevel.Enthusiastic => "Muestras entusiasmo genuino.",
            _ => "Mantienes un tono cordial.",
        };

        return $"{formality} {verbosity} {energy}";
    }

    /// <summary>
    /// The one place the named levels become numbers. Tuning these is a backend change
    /// with no frontend release, which is the entire point of storing the style.
    ///
    /// Formality and energy both push variety, so they add: a warm, enthusiastic assistant
    /// should not sample like a formal, neutral one. Verbosity does not appear here —
    /// length is a token budget, not a temperature.
    /// </summary>
    private static decimal TemperatureFor(AgentStyle style)
    {
        var temperature = BaseTemperature
            + style.Formality switch
            {
                FormalityLevel.Formal => 0.00m,
                FormalityLevel.Warm => 0.20m,
                _ => 0.10m,
            }
            + style.Energy switch
            {
                EnergyLevel.Neutral => 0.00m,
                EnergyLevel.Enthusiastic => 0.25m,
                _ => 0.10m,
            };

        return Math.Clamp(temperature, 0m, MaxTemperature);
    }

    /// <summary>
    /// Verbosity is the only axis that costs money per reply, so it is the only one that
    /// moves the budget. Brief is deliberately tight: on WhatsApp a wall of text is worse
    /// than a short answer, not better.
    /// </summary>
    private static int MaxTokensFor(AgentStyle style) => style.Verbosity switch
    {
        VerbosityLevel.Brief => 300,
        VerbosityLevel.Detailed => 1_600,
        _ => DefaultMaxTokens,
    };

    private const decimal BaseTemperature = 0.10m;

    private const decimal MaxTemperature = 2.00m;

    private static void ValidateText(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Trim().Length > maxLength)
        {
            throw new ArgumentException($"Must be at most {maxLength} characters.", parameterName);
        }
    }
}
