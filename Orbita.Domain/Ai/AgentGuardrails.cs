namespace Orbita.Domain.Ai;

/// <summary>
/// ORB-C06. The rules that decide when an assistant must not answer, and when what it
/// produced must not be sent.
///
/// Pure domain logic with no dependencies: every check is a function of the assistant's
/// configuration, the customer's message and what has already been said. That is on
/// purpose — these are the rules a person would state about their own business ("no
/// hables de dosis", "no contestes veinte veces seguidas"), not an infrastructure
/// concern, and keeping them here makes them testable without a model or a database.
///
/// Two of the five acceptance criteria are not code here, and should not be:
/// "ninguna respuesta contiene datos de otro cliente" is enforced by construction —
/// retrieval is scoped by tenant *and* agent, under Row Level Security — so what it
/// needs is a test that would fail if that ever stopped being true, not a filter that
/// pretends to catch a leak after the fact. A filter would also be the wrong shape: it
/// would have to know what another customer's data looks like.
/// </summary>
public static class AgentGuardrails
{
    /// <summary>
    /// WhatsApp rejects a text body longer than this, so a reply past it is not "long",
    /// it is undeliverable. Meta's own limit, not a preference of ours.
    /// </summary>
    public const int MaxReplyLength = 4_096;

    /// <summary>
    /// How many times the assistant may answer one conversation within a single service
    /// window. Reached only by a customer messaging relentlessly or by another bot on the
    /// far end, and both are cases where a person should look rather than the meter keep
    /// running.
    /// </summary>
    public const int MaxRepliesPerWindow = 30;

    /// <summary>
    /// How many assistant turns in a row, with nothing from the customer between them,
    /// count as a loop. Two is a coincidence; three is the assistant talking to itself.
    /// </summary>
    public const int MaxConsecutiveAssistantTurns = 3;

    /// <summary>
    /// Checked before the model is called, so a question the assistant must not touch
    /// costs nothing.
    /// </summary>
    public static GuardrailVerdict InspectIncoming(
        AiAgent agent,
        string incomingMessage,
        int repliesInWindow,
        IReadOnlyList<AgentConversationTurn> history)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(history);

        if (repliesInWindow >= MaxRepliesPerWindow)
        {
            return GuardrailVerdict.Block(GuardrailReason.TooManyRepliesInWindow);
        }

        if (ConsecutiveAssistantTurns(history) >= MaxConsecutiveAssistantTurns)
        {
            return GuardrailVerdict.Block(GuardrailReason.LoopDetected);
        }

        if (MatchedBlockedTopic(agent, incomingMessage) is { } topic)
        {
            return GuardrailVerdict.Block(GuardrailReason.OutOfScopeTopic, topic);
        }

        return GuardrailVerdict.Allowed;
    }

    /// <summary>
    /// Checked after the model answers. "Validación de la salida contra un esquema" for a
    /// chat reply means something narrower than JSON: one plain text block, deliverable
    /// by the channel, that is not the assistant repeating itself and does not carry the
    /// scaffolding we wrapped around its instructions.
    /// </summary>
    public static GuardrailVerdict InspectReply(string reply, IReadOnlyList<AgentConversationTurn> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (string.IsNullOrWhiteSpace(reply))
        {
            return GuardrailVerdict.Block(GuardrailReason.EmptyReply);
        }

        if (reply.Length > MaxReplyLength)
        {
            return GuardrailVerdict.Block(GuardrailReason.ReplyTooLong);
        }

        if (LeaksScaffolding(reply))
        {
            return GuardrailVerdict.Block(GuardrailReason.LeakedPromptScaffolding);
        }

        // A model that answers the same sentence twice has stopped making progress, and
        // the customer is watching it happen.
        var lastAssistantTurn = history.LastOrDefault(turn => turn.FromAssistant);

        if (lastAssistantTurn is not null && IsEffectivelyTheSame(lastAssistantTurn.Content, reply))
        {
            return GuardrailVerdict.Block(GuardrailReason.RepeatedItself);
        }

        return GuardrailVerdict.Allowed;
    }

    /// <summary>
    /// Whole-word match, case- and accent-insensitive.
    ///
    /// Accent-insensitive because somebody who types "diagnostico" in a hurry means
    /// "diagnóstico", and a rule that misses on a missing accent is a rule the business
    /// thinks it has and does not.
    ///
    /// Whole-word rather than substring, and this one matters more than it looks: a
    /// boutique blocking "talla" would fire on "pantalla", a bakery blocking "precio" on
    /// "apreciamos", a clinic blocking "cita" on "felicitaciones". The failure mode is the
    /// worst kind — the assistant does not break, it goes quiet, answering the
    /// out-of-scope line to ordinary messages, and the owner has no way to connect that to
    /// a word she typed three weeks ago. With fifty topics allowed, a collision stops
    /// being hypothetical.
    ///
    /// The cost is real and accepted: "precio" no longer catches "precios". Erring toward
    /// not blocking is recoverable — the owner adds the plural and watches it work. Erring
    /// toward blocking leaves an assistant mute with no diagnosis.
    /// </summary>
    public static string? MatchedBlockedTopic(AiAgent agent, string? incomingMessage)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (agent.BlockedTopics.Count == 0 || string.IsNullOrWhiteSpace(incomingMessage))
        {
            return null;
        }

        return agent.BlockedTopics.FirstOrDefault(topic => Common.WholeWordText.Contains(incomingMessage, topic));
    }

    private static int ConsecutiveAssistantTurns(IReadOnlyList<AgentConversationTurn> history)
    {
        var count = 0;

        for (var i = history.Count - 1; i >= 0 && history[i].FromAssistant; i--)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// The grounding block wraps each passage as <c>[Título del documento] texto</c> and
    /// the standing rules are a bulleted list addressed to the model. Either one showing
    /// up in an answer means the model transcribed its instructions instead of following
    /// them — rare, but it exposes the business's documents verbatim to a customer.
    /// </summary>
    private static bool LeaksScaffolding(string reply)
        => reply.Contains("Estás respondiendo por chat a un cliente real", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("Estos son fragmentos de los documentos del negocio", StringComparison.OrdinalIgnoreCase);

    private static bool IsEffectivelyTheSame(string previous, string candidate)
        => string.Equals(Common.WholeWordText.Normalize(previous), Common.WholeWordText.Normalize(candidate), StringComparison.Ordinal);
}

/// <summary>One earlier turn, reduced to what the guardrails need to judge it.</summary>
public sealed record AgentConversationTurn(bool FromAssistant, string Content);

/// <summary>Whether the assistant may proceed, and if not, why.</summary>
public sealed record GuardrailVerdict(bool IsAllowed, GuardrailReason? Reason = null, string? MatchedTopic = null)
{
    public static GuardrailVerdict Allowed { get; } = new(true);

    public static GuardrailVerdict Block(GuardrailReason reason, string? matchedTopic = null)
        => new(false, reason, matchedTopic);
}

public enum GuardrailReason
{
    /// <summary>The owner declared this subject out of scope for their assistant.</summary>
    OutOfScopeTopic,

    /// <summary>The assistant has already answered this conversation too many times in one window.</summary>
    TooManyRepliesInWindow,

    /// <summary>Several assistant turns in a row with nothing from the customer between them.</summary>
    LoopDetected,

    /// <summary>The model produced nothing to send.</summary>
    EmptyReply,

    /// <summary>Longer than the channel will accept.</summary>
    ReplyTooLong,

    /// <summary>The answer transcribed its own instructions or the retrieved passages.</summary>
    LeakedPromptScaffolding,

    /// <summary>The same answer the assistant already gave.</summary>
    RepeatedItself,
}
