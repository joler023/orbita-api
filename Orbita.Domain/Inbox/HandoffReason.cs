namespace Orbita.Domain.Inbox;

/// <summary>
/// Why a conversation stopped being the assistant's and started being a person's
/// (ORB-C07). The four values are the four triggers the story names, in the same order:
/// "por petición explícita del cliente, por detección de frustración, por tema fuera de
/// alcance o por decisión del agente".
///
/// It is recorded, not derived, because the answer changes what the person picking the
/// conversation up should do: <see cref="CustomerAsked"/> means someone is waiting and
/// knows it, <see cref="OutOfScopeTopic"/> means the business itself decided a machine
/// must not answer this, and <see cref="AgentDecision"/> means the assistant tried and
/// the customer got nothing.
/// </summary>
public enum HandoffReason
{
    /// <summary>The customer asked for a person, in words.</summary>
    CustomerAsked,

    /// <summary>The customer is visibly stuck or annoyed with the assistant.</summary>
    Frustration,

    /// <summary>A subject the owner declared off-limits for the assistant (ORB-C06).</summary>
    OutOfScopeTopic,

    /// <summary>
    /// The assistant itself gave up: it called <c>escalar_a_humano</c>, looped, exhausted
    /// its replies for the window, or produced something unsendable. What they share is
    /// that the customer's message went unanswered, which is the exact "círculo" this
    /// story exists to break.
    /// </summary>
    AgentDecision,
}
