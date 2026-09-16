using Orbita.Domain.Common;
using Orbita.Domain.Inbox;

namespace Orbita.Domain.Ai;

/// <summary>
/// ORB-C07. When a conversation should stop being the assistant's, decided before any
/// model is called.
///
/// Pure, like <see cref="AgentGuardrails"/>, and for the same reasons: these are business
/// rules about when a machine should step aside, they are testable without a model or a
/// database, and they must cost nothing — a customer asking for a person should not have
/// to wait for a model call to be put through.
///
/// <para><b>Why phrases and not a classifier.</b> "Detección de frustración" invites a
/// model call per inbound message, which would roughly double the cost of every
/// conversation in the product to catch a case that happens in a few of them. A curated
/// phrase list catches the unambiguous half for free. The half it misses — sarcasm,
/// politeness masking anger — is exactly what the assistant itself is better at spotting,
/// and that path already exists: <c>escalar_a_humano</c> lets the model hand over on its
/// own judgment.</para>
///
/// <para><b>Which way it errs.</b> Toward <em>not</em> handing over. A false positive
/// takes a conversation away from the assistant until a person gives it back, so a
/// phrase that fires on an ordinary message costs the business an answer it could have
/// given instantly. A false negative costs one more assistant turn before the customer
/// says it more plainly. The list is therefore short and specific — whole phrases, not
/// single angry-sounding words — and <c>ya te dije</c>-style repetition is left to the
/// objective check below rather than to guesswork about tone.</para>
/// </summary>
public static class HandoffTriggers
{
    /// <summary>
    /// How many times the customer has to send effectively the same message before it
    /// counts as being stuck. Two is someone correcting a typo; three is someone who has
    /// already watched the assistant fail twice.
    /// </summary>
    public const int RepeatedMessagesBeforeHandoff = 3;

    /// <summary>
    /// Asking for a person, in words. Kept to phrases rather than words like "persona" or
    /// "asesor" alone, which appear constantly in ordinary sentences ("la persona que me
    /// atendió ayer", "mi asesor comercial").
    /// </summary>
    private static readonly string[] ExplicitRequests =
    [
        "hablar con una persona",
        "hablar con alguien",
        "hablar con un humano",
        "hablar con un asesor",
        "hablar con un agente",
        "hablar con un representante",
        "una persona real",
        "un humano de verdad",
        "atención humana",
        "quiero un humano",
        "quiero una persona",
        "necesito una persona",
        "necesito hablar con alguien",
        "pásame con alguien",
        "pásame con una persona",
        "pásame con un asesor",
        "comunicarme con alguien",
        "comunicarme con una persona",
        "me comunicas con alguien",
        "hay alguien ahí",
    ];

    /// <summary>
    /// Being stuck or annoyed, said plainly. Insults are included as whole words because
    /// somebody who insults the assistant has already decided it is not helping them.
    /// </summary>
    private static readonly string[] Frustration =
    [
        "no me estás entendiendo",
        "no me entiendes",
        "no entiendes nada",
        "no me estás ayudando",
        "no me ayudas en nada",
        "no sirves",
        "no me sirves",
        "esto no sirve",
        "eres un robot",
        "eres un bot",
        "sos un robot",
        "sos un bot",
        "no quiero un bot",
        "no quiero hablar con un robot",
        "es la tercera vez",
        "inútil",
        "estúpido",
        "idiota",
        "una porquería",
    ];

    /// <summary>
    /// Whether this message should take the conversation away from the assistant, and why.
    /// Null means carry on.
    ///
    /// An explicit request wins over frustration when both match: it is the more precise
    /// reading of the same message, and it is the one the person picking the conversation
    /// up can act on ("me pidieron un humano" beats "parecía molesto").
    /// </summary>
    /// <param name="history">Earlier turns, oldest first. Only the customer's own are read.</param>
    public static HandoffReason? Detect(string? incomingMessage, IReadOnlyList<AgentConversationTurn> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (string.IsNullOrWhiteSpace(incomingMessage))
        {
            return null;
        }

        if (ExplicitRequests.Any(phrase => WholeWordText.Contains(incomingMessage, phrase)))
        {
            return HandoffReason.CustomerAsked;
        }

        if (Frustration.Any(phrase => WholeWordText.Contains(incomingMessage, phrase)))
        {
            return HandoffReason.Frustration;
        }

        return HasRepeatedItself(incomingMessage, history) ? HandoffReason.Frustration : null;
    }

    /// <summary>
    /// What a guardrail block (ORB-C06) means for the handoff.
    ///
    /// Every one of them hands the conversation over, because they share a consequence:
    /// the customer wrote something and got either a refusal or nothing at all. The reason
    /// differs, and it is worth keeping apart — an out-of-scope subject is the business's
    /// own decision working as intended, while a loop or an unsendable answer is the
    /// assistant failing — but in both cases a person now has to look.
    /// </summary>
    public static HandoffReason ForGuardrail(GuardrailReason reason)
        => reason == GuardrailReason.OutOfScopeTopic
            ? HandoffReason.OutOfScopeTopic
            : HandoffReason.AgentDecision;

    private static bool HasRepeatedItself(string incomingMessage, IReadOnlyList<AgentConversationTurn> history)
    {
        var normalized = WholeWordText.Normalize(incomingMessage);

        if (normalized.Length == 0)
        {
            return false;
        }

        var earlier = history.Count(turn =>
            !turn.FromAssistant
            && string.Equals(WholeWordText.Normalize(turn.Content), normalized, StringComparison.Ordinal));

        // The message being judged is not in the history yet, so it counts as one itself.
        return earlier + 1 >= RepeatedMessagesBeforeHandoff;
    }
}
