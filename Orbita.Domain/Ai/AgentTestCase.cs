using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// ORB-C11: an exchange an owner saved to run again after changing their assistant —
/// "se pueden guardar casos de prueba y volver a ejecutarlos tras un cambio".
///
/// It lives on the server, not in the browser, and the criterion is what decides it: the
/// value of a saved case is re-running it <em>after</em> a change, and local storage loses
/// it exactly when it matters — on another machine, in another browser, after clearing
/// site data. It is also the owner's work, not a preference of their browser.
///
/// Running one is not a new endpoint: the screen reads the case and posts it to the test
/// bench that already exists, so there is one path a test exchange can take and no second
/// way for the prompt to be built.
/// </summary>
public sealed class AgentTestCase : Entity
{
    public const int NameMaxLength = 80;

    public const int MaxTurns = 40;

    /// <summary>Per assistant. A library nobody can read is not a library.</summary>
    public const int MaxPerAgent = 20;

    private AgentTestCase(Guid id, Guid tenantId, Guid agentId, string name, IReadOnlyList<AgentTestTurn> turns, DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        AgentId = agentId;
        Name = name;
        Turns = turns;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; }

    public Guid AgentId { get; }

    /// <summary>What the owner called it. Never derived from the first message: a case
    /// called "hola" tells nobody what it is for six weeks later.</summary>
    public string Name { get; private set; }

    /// <summary>The exchange, oldest first, in the same shape the test bench takes.</summary>
    public IReadOnlyList<AgentTestTurn> Turns { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <exception cref="ArgumentException">No name, no turns, or past the limits.</exception>
    public static AgentTestCase Create(Guid tenantId, Guid agentId, string name, IReadOnlyList<AgentTestTurn> turns, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(turns);

        var trimmed = name.Trim();

        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException($"El nombre no puede pasar de {NameMaxLength} caracteres.", nameof(name));
        }

        if (turns.Count == 0)
        {
            throw new ArgumentException("Un caso de prueba sin mensajes no prueba nada.", nameof(turns));
        }

        if (turns.Count > MaxTurns)
        {
            throw new ArgumentException($"Un caso de prueba admite hasta {MaxTurns} mensajes.", nameof(turns));
        }

        if (turns.Any(turn => string.IsNullOrWhiteSpace(turn.Content)))
        {
            throw new ArgumentException("Ningún mensaje del caso puede estar vacío.", nameof(turns));
        }

        return new AgentTestCase(Guid.NewGuid(), tenantId, agentId, trimmed, [.. turns], now);
    }
}

public interface IAgentTestCaseRepository
{
    Task<IReadOnlyList<AgentTestCase>> ListByAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken);

    Task<AgentTestCase?> GetByIdAsync(Guid tenantId, Guid testCaseId, CancellationToken cancellationToken);

    Task<int> CountByAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken);

    void Add(AgentTestCase testCase);

    void Remove(AgentTestCase testCase);
}
