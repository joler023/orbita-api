using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// ORB-C11: trying an assistant out before customers do — screen 2.8's "banco de pruebas".
///
/// <para>Deliberately <b>not</b> ORB-C04.</para> C04 is the assistant answering a real
/// customer on a real channel, and it needs conversations and messages that Track B has not
/// merged yet. This is an owner talking to their own assistant in a sandbox: nothing is
/// persisted beyond the <c>ai_runs</c> measurement, and the caller supplies the history, so
/// none of that is blocked.
///
/// Requires <c>Permission.ManageAiAgents</c>, like the rest of ORB-C10 — this is a
/// configuration tool, not a customer-facing surface.
/// </summary>
public interface IAgentTestBenchService
{
    /// <exception cref="AiAgentNotFoundException">No such agent in this tenant.</exception>
    /// <exception cref="LlmProviderException">Every configured provider failed.</exception>
    Task<AgentTestResult> RunAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        AgentTestRequest request,
        CancellationToken cancellationToken);
}

/// <param name="Message">What the operator typed, as a customer would write it.</param>
/// <param name="History">
/// The exchange so far, oldest first — supplied by the caller because a test bench is
/// ephemeral by definition. Persisting it would mean inventing a table orbita-schema.dbml
/// does not have, for transcripts nobody asked to keep.
/// </param>
public sealed record AgentTestRequest(string Message, IReadOnlyList<AgentTestTurn> History);

/// <param name="Reply">What the assistant answered.</param>
/// <param name="Retrieved">
/// The passages that were pulled from the knowledge base and put in front of the model,
/// with score and source. Empty when the assistant has the knowledge lookup turned off, or
/// when it has nothing indexed.
/// </param>
/// <param name="ToolCalls">
/// What actually ran, in order. Today that is only the knowledge lookup: the other four
/// tools in <see cref="AiToolCatalog"/> depend on modules that do not exist (ORB-D05's
/// opportunities, ORB-B03's inbox) and report <c>IsAvailable = false</c>, so an assistant
/// cannot have them enabled in the first place. Letting the model call tools itself is
/// ORB-C05.
/// </param>
/// <param name="Usage">What the exchange cost. Screen 2.8 is a diagnostic screen for an
/// owner tuning their assistant, so unlike screens 2.5 and 2.6 it may show this.</param>
/// <param name="TestedDraft">
/// True when the answer came from unpublished changes rather than the live configuration,
/// so the screen can say which one the owner is looking at.
/// </param>
public sealed record AgentTestResult(
    string Reply,
    IReadOnlyList<KnowledgeSearchHit> Retrieved,
    IReadOnlyList<AgentToolCallTrace> ToolCalls,
    AgentTestUsage Usage,
    bool TestedDraft);

/// <param name="Tool">A key from <see cref="AiToolCatalog"/>.</param>
/// <param name="Summary">What it did, in Spanish, for an operator rather than a developer.</param>
public sealed record AgentToolCallTrace(string Tool, string Summary);

/// <summary>
/// The totals for the whole exchange — the embedding of the question plus the reply — since
/// that is what one press of "Enviar" actually costs.
///
/// The model's name is not here on purpose: which model serves which task is a backend
/// decision (ORB-C13, tunable per tenant), and putting it on screen would invite the UI to
/// start caring.
/// </summary>
public sealed record AgentTestUsage(int TokensIn, int TokensOut, decimal CostUsd, int LatencyMs);
