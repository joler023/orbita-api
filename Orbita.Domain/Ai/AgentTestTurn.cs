namespace Orbita.Domain.Ai;

/// <summary>
/// One turn of a test exchange (ORB-C11). In the domain rather than the application layer
/// because <see cref="AgentTestCase"/> persists it: the saved case and the exchange posted
/// to the test bench are the same shape on purpose, so the screen never has to translate
/// between two forms of the same history.
/// </summary>
public sealed record AgentTestTurn(AgentTestRole Role, string Content);

/// <summary>
/// Only the two roles an operator can produce. The system prompt is composed by the backend
/// and tool results are recorded by it, so neither is something a caller may inject —
/// accepting a <c>System</c> turn here would let the screen rewrite the assistant's
/// standing instructions for one message and see a reply the real agent would never give.
/// </summary>
public enum AgentTestRole
{
    User,

    Assistant,
}
