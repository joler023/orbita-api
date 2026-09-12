using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// A configurable AI assistant belonging to one tenant (orbita-schema.dbml's
/// <c>ai_agents</c>), and the thing ORB-C10's configuration screen edits.
///
/// <para><b>Two vocabularies, one row.</b> The owner chooses a <see cref="Personality"/>,
/// some <see cref="Instructions"/> and a <see cref="Tone"/>. The model needs a
/// <see cref="SystemPrompt"/> and a <see cref="Temperature"/>. This entity holds both and
/// keeps the second derived from the first, so neither side has to know about the other:
/// the screen never shows the words "prompt" or "temperature" (a hard product rule —
/// ORB-C10 is described as the second hardest screen in the product precisely because it
/// asks a non-technical person to configure an AI), and ORB-C04 can read
/// <c>SystemPrompt</c> without knowing how it was assembled.</para>
///
/// <para>Keeping the derived values in columns rather than computing them at read time is
/// deliberate: orbita-schema.dbml specifies <c>system_prompt</c> and <c>temperature</c> as
/// real columns, and anything reading the table directly — a worker, a migration, an
/// analyst — should find a usable value there.</para>
///
/// <see cref="IsEnabled"/> defaults to false: an assistant that starts answering customers
/// the moment an organization is created would be a nasty surprise.
/// </summary>
public sealed class AiAgent : Entity
{
    private readonly List<string> _tools = [];

    private AiAgent(
        Guid id,
        Guid tenantId,
        string name,
        string personality,
        string instructions,
        AgentTone tone,
        bool isEnabled,
        DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Personality = personality;
        Instructions = instructions;
        Tone = tone;
        Temperature = TemperatureFor(tone);
        IsEnabled = isEnabled;
        CreatedAt = createdAt;
        SystemPrompt = ComposePrompt(name, personality, instructions);
    }

    public Guid TenantId { get; }

    public string Name { get; private set; }

    /// <summary>How the assistant should come across. Free text written by the owner.</summary>
    public string Personality { get; private set; }

    /// <summary>What it should and should not do. Free text written by the owner.</summary>
    public string Instructions { get; private set; }

    public AgentTone Tone { get; private set; }

    /// <summary>
    /// The standing instructions actually sent to the model, composed from
    /// <see cref="Personality"/> and <see cref="Instructions"/>. Never edited directly and
    /// never shown to a user.
    /// </summary>
    public string SystemPrompt { get; private set; }

    /// <summary>
    /// Derived from <see cref="Tone"/> and rewritten whenever it changes. Stored rather
    /// than computed so anything reading <c>ai_agents</c> directly finds a real value in
    /// the column orbita-schema.dbml specifies.
    /// </summary>
    public decimal Temperature { get; private set; }

    public int MaxTokens { get; private set; } = DefaultMaxTokens;

    /// <summary>
    /// Keys from <see cref="AiToolCatalog"/> that this assistant may call. Only tools that
    /// actually work can be here — see <see cref="EnableTools"/>.
    /// </summary>
    public IReadOnlyList<string> Tools => _tools;

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Defaults from orbita-schema.dbml's <c>ai_agents</c> definition.</summary>
    public const int DefaultMaxTokens = 800;

    public const int NameMaxLength = 120;

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
            $"Eres el asistente virtual de {businessName}. Respondes con amabilidad y brevedad.",
            "Respondes en el idioma en que te escriban. Si no sabes algo, lo dices y ofreces "
                + "pasar la conversación a una persona del equipo. Nunca inventas información.",
            AgentTone.Balanced,
            now);

        agent.EnableTools([AiToolCatalog.ConsultarConocimiento]);

        return agent;
    }

    public static AiAgent Create(
        Guid tenantId,
        string name,
        string personality,
        string instructions,
        AgentTone tone,
        DateTimeOffset now)
    {
        ValidateText(name, NameMaxLength, nameof(name));
        ValidateText(personality, PersonalityMaxLength, nameof(personality));
        ValidateText(instructions, InstructionsMaxLength, nameof(instructions));

        return new AiAgent(
            Guid.NewGuid(), tenantId, name.Trim(), personality.Trim(), instructions.Trim(), tone, isEnabled: false, now);
    }

    public void Rename(string name)
    {
        ValidateText(name, NameMaxLength, nameof(name));
        Name = name.Trim();
        SystemPrompt = ComposePrompt(Name, Personality, Instructions);
    }

    public void Reconfigure(string personality, string instructions, AgentTone tone)
    {
        ValidateText(personality, PersonalityMaxLength, nameof(personality));
        ValidateText(instructions, InstructionsMaxLength, nameof(instructions));

        Personality = personality.Trim();
        Instructions = instructions.Trim();
        Tone = tone;
        Temperature = TemperatureFor(tone);
        SystemPrompt = ComposePrompt(Name, Personality, Instructions);
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
    /// Turning an assistant on is what puts it in front of customers, so it is its own
    /// operation rather than a field on a bulk update — the list screen toggles it
    /// directly, without loading the rest.
    /// </summary>
    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    /// <summary>
    /// Assembles what the model actually receives. Kept here, in the domain, because it is
    /// the rule that turns two product concepts into one model concept — not a
    /// presentation detail and not a persistence one.
    /// </summary>
    private static string ComposePrompt(string name, string personality, string instructions)
        => $"""
            Te llamas {name}.

            {personality}

            {instructions}
            """;

    /// <summary>
    /// The one place the named levels become numbers. Tuning these is a backend change
    /// with no frontend release, which is the entire point of storing the tone.
    /// </summary>
    private static decimal TemperatureFor(AgentTone tone) => tone switch
    {
        AgentTone.Formal => 0.10m,
        AgentTone.Balanced => 0.30m,
        AgentTone.Conversational => 0.70m,
        _ => throw new ArgumentOutOfRangeException(nameof(tone), tone, "Unmapped agent tone."),
    };

    private static void ValidateText(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Trim().Length > maxLength)
        {
            throw new ArgumentException($"Must be at most {maxLength} characters.", parameterName);
        }
    }
}
