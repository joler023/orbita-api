namespace Orbita.Application.Ai;

/// <summary>
/// ORB-C04: the assistant answering a real customer on a real conversation.
///
/// Sibling of ORB-C11's <see cref="IAgentTestBenchService"/>, and the difference is the
/// whole point: the test bench is driven by an owner and answers into a screen, this is
/// driven by an inbound message and answers into the outbound queue.
/// </summary>
public interface IAgentConversationResponder
{
    /// <summary>
    /// Decides whether this inbound message deserves an assistant reply and, if so,
    /// writes one.
    ///
    /// Never throws for "the assistant should stay quiet" — a conversation with no
    /// assistant, a disabled one, or a closed service window are ordinary states, not
    /// failures, and treating them as exceptions would fill the dispatcher's log with
    /// errors for the most common case in the product. A model or channel failure does
    /// throw: that one is worth retrying and worth seeing.
    /// </summary>
    Task<AgentReplyOutcome> RespondAsync(
        Guid tenantId,
        Guid conversationId,
        Guid inboundMessageId,
        CancellationToken cancellationToken);
}

/// <summary>Why the assistant stayed quiet, or the reply it produced.</summary>
public sealed record AgentReplyOutcome(
    AgentReplyDecision Decision,
    Guid? MessageId = null,
    Domain.Ai.GuardrailReason? GuardrailReason = null)
{
    public static AgentReplyOutcome Replied(Guid messageId) => new(AgentReplyDecision.Replied, messageId);

    public static AgentReplyOutcome Skipped(AgentReplyDecision decision) => new(decision);

    public static AgentReplyOutcome Blocked(Domain.Ai.GuardrailReason reason)
        => new(AgentReplyDecision.BlockedByGuardrail, null, reason);
}

public enum AgentReplyDecision
{
    Replied,

    /// <summary>The message the event pointed at is gone, or is not an inbound one.</summary>
    NotAnInboundMessage,

    /// <summary>Nothing to answer: an image with no caption, for instance.</summary>
    NothingToAnswer,

    /// <summary>The event pointed at a conversation that no longer exists.</summary>
    ConversationGone,

    /// <summary>Neither the conversation nor the tenant has an enabled assistant.</summary>
    NoAgentAssigned,

    /// <summary>The assistant exists but the owner switched it off (ORB-C10).</summary>
    AgentDisabled,

    /// <summary>A person is already handling this conversation — ORB-C07 will formalize this.</summary>
    HumanIsHandlingIt,

    /// <summary>Outside the 24h service window a free-form reply is impossible (ORB-B07).</summary>
    ServiceWindowClosed,

    /// <summary>The model answered with nothing at all — no text to send.</summary>
    ModelProducedNoText,

    /// <summary>
    /// A guardrail stopped it (ORB-C06). <see cref="AgentReplyOutcome.GuardrailReason"/>
    /// says which one: an out-of-scope subject the owner declared, too many replies in
    /// one window, a loop, or a reply that failed validation.
    /// </summary>
    BlockedByGuardrail,
}
