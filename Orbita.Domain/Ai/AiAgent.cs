using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// A configurable AI assistant belonging to one tenant (orbita-schema.dbml's
/// <c>ai_agents</c>).
///
/// ORB-C02 needs this to exist because <c>knowledge_docs.agent_id</c> and
/// <c>ai_runs.agent_id</c> are both <c>NOT NULL</c> — knowledge hangs off an agent, not
/// off a tenant. Building the *management* surface for it (naming it, writing its
/// instructions, choosing its tools) is ORB-C10's job, so the mutators here cover only
/// what C02 actually exercises. A tenant gets one disabled agent seeded at registration,
/// the same way ORB-D04 seeds a default pipeline "para que nadie empiece con la pantalla
/// vacía".
///
/// <see cref="IsEnabled"/> defaults to false on purpose: an assistant that starts
/// answering customers the moment an organization is created would be a nasty surprise.
/// </summary>
public sealed class AiAgent : Entity
{
    private AiAgent(
        Guid id,
        Guid tenantId,
        string name,
        string systemPrompt,
        decimal temperature,
        int maxTokens,
        bool isEnabled,
        DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        SystemPrompt = systemPrompt;
        Temperature = temperature;
        MaxTokens = maxTokens;
        IsEnabled = isEnabled;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; }

    public string Name { get; private set; }

    /// <summary>
    /// The standing instructions sent to the model. Composed by the backend from what
    /// ORB-C10's UI collects — that screen never shows the word "prompt", so this is
    /// never edited raw by a user.
    /// </summary>
    public string SystemPrompt { get; private set; }

    /// <summary>
    /// Sampling temperature. The product surface asks "¿qué tan creativo quieres que
    /// sea?" and maps three named levels onto this number — the number itself is never
    /// shown, so changing the mapping never needs a frontend release.
    /// </summary>
    public decimal Temperature { get; private set; }

    public int MaxTokens { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The default assistant seeded when an organization registers. Disabled, with
    /// neutral instructions, so the owner finds something to configure rather than an
    /// empty screen.
    /// </summary>
    public static AiAgent CreateDefault(Guid tenantId, string businessName, DateTimeOffset now)
        => Create(
            tenantId,
            "Asistente",
            $"Eres el asistente virtual de {businessName}. Respondes con amabilidad y brevedad, "
                + "en el idioma en que te escriban. Si no sabes algo, lo dices y ofreces pasar la "
                + "conversación a una persona del equipo. Nunca inventas información.",
            now);

    public static AiAgent Create(
        Guid tenantId,
        string name,
        string systemPrompt,
        DateTimeOffset now,
        decimal temperature = DefaultTemperature,
        int maxTokens = DefaultMaxTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentOutOfRangeException.ThrowIfNegative(temperature);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(temperature, MaxTemperature);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 1);

        return new AiAgent(Guid.NewGuid(), tenantId, name.Trim(), systemPrompt.Trim(), temperature, maxTokens, isEnabled: false, now);
    }

    /// <summary>Defaults from orbita-schema.dbml's <c>ai_agents</c> definition.</summary>
    public const decimal DefaultTemperature = 0.30m;

    public const int DefaultMaxTokens = 800;

    /// <summary>Above this, every provider degenerates into noise.</summary>
    public const decimal MaxTemperature = 2.00m;
}
