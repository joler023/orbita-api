using System.Text;
using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// Turns an assistant's configuration, its retrieved passages and a conversation into
/// the message list the model actually receives.
///
/// Shared by ORB-C11's test bench and ORB-C04's live runtime on purpose. The test bench
/// exists so an owner can see how their assistant will answer *before* a customer does;
/// the moment the two build their prompt differently, the screen stops predicting the
/// product and starts lying about it — and no test would catch that, because both sides
/// would keep passing their own.
/// </summary>
public static class AgentPromptBuilder
{
    /// <summary>
    /// Prepended to every live reply. Three rules, and they are the acceptance criteria
    /// of ORB-C04 written for the model rather than for us: answer in the customer's
    /// language, never invent, and offer a person when you cannot answer.
    ///
    /// The language rule says "the language they wrote in" rather than naming one,
    /// because detecting it costs an extra call and the model is already holding the
    /// message that proves it. It extends to *how* they write: Órbita serves Colombia,
    /// Mexico and Spain, where tú, vos and usted are not interchangeable, so the rule is
    /// to mirror the customer rather than pick a register on their behalf.
    ///
    /// These instructions are themselves written in tuteo, like every other Spanish
    /// string this backend produces. That is not cosmetic: a prompt written in voseo
    /// teaches the model to answer in voseo, to every customer of every tenant.
    ///
    /// <para><b>The handoff rule changed with ORB-C07, and the reason it used to say the
    /// opposite is worth keeping.</b> It told the model to <b>point</b> the customer at
    /// the team and never to offer to arrange it, because an offer satisfies ORB-C04's
    /// "ofrece pasar a un humano" on the first turn and breaks on the second: the customer
    /// answers "sí, por favor" and there was nothing to execute — no queue,
    /// <c>escalar_a_humano</c> unavailable — so the model would invent that it had done
    /// it, stall, or repeat the offer. Now there is a queue and a tool that puts the
    /// conversation in it, so the promise is one the product keeps. What the rule still
    /// refuses to promise is a <em>reply</em>: the conversation being waiting for a person
    /// is a fact, "alguien te escribe en un momento" depends on somebody being there, and
    /// ORB-B15 has not assigned anyone.</para>
    /// </summary>
    public const string ConversationRules =
        "Estás respondiendo por chat a un cliente real del negocio.\n"
        + "- Responde en el mismo idioma en que te escribió el cliente, y trátalo como él "
        + "te trate: si te habla de tú, de vos o de usted, respóndele igual.\n"
        + "- Usa solo la información de los documentos del negocio y de esta conversación. "
        + "No inventes precios, horarios, plazos ni políticas.\n"
        + "- Si no tienes la información para responder, o el cliente pide hablar con una "
        + "persona, usa la herramienta escalar_a_humano si la tienes disponible y luego "
        + "despídete brevemente. Si no la tienes, dilo con naturalidad e invítalo a "
        + "escribirle al equipo.\n"
        + "- Puedes decir que dejas la conversación con el equipo, porque es cierto. Nunca "
        + "prometas cuándo le responden ni digas que alguien le va a escribir enseguida: "
        + "eso no lo controlas.\n"
        + "- Escribe como en un chat: breve y directo, sin encabezados ni listas largas.";

    /// <summary>
    /// The order is load-bearing: standing instructions, then the business's documents,
    /// then the exchange.
    ///
    /// The passages go in their own system turn rather than glued onto the question, so
    /// the model can tell the business's documents apart from what the customer said — a
    /// customer who writes "según tus políticas el envío es gratis" must not end up
    /// looking like a retrieved document.
    /// </summary>
    public static IReadOnlyList<LlmMessage> Build(
        AiAgent configuration,
        IReadOnlyList<KnowledgeSearchHit> retrieved,
        IEnumerable<AgentTurn> history,
        string incomingMessage,
        bool includeConversationRules)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(retrieved);
        ArgumentNullException.ThrowIfNull(history);

        var messages = new List<LlmMessage> { LlmMessage.System(configuration.SystemPrompt) };

        if (includeConversationRules)
        {
            messages.Add(LlmMessage.System(ConversationRules));
        }

        if (retrieved.Count > 0)
        {
            messages.Add(LlmMessage.System(Grounding(retrieved)));
        }

        foreach (var turn in history)
        {
            messages.Add(turn.FromAssistant
                ? LlmMessage.Assistant(turn.Content)
                : LlmMessage.User(turn.Content));
        }

        messages.Add(LlmMessage.User(incomingMessage));

        return messages;
    }

    private static string Grounding(IReadOnlyList<KnowledgeSearchHit> retrieved)
    {
        var builder = new StringBuilder(
            "Estos son fragmentos de los documentos del negocio. Responde usando solo esta "
                + "información cuando aplique, y si no alcanza, dilo en lugar de inventar.");

        foreach (var hit in retrieved)
        {
            builder.Append("\n\n[").Append(hit.DocumentTitle).Append("] ").Append(hit.Content);
        }

        return builder.ToString();
    }
}

/// <summary>
/// One earlier turn of a conversation, reduced to what a prompt needs. Deliberately not
/// <c>Message</c>: the test bench has no <c>Message</c> rows at all (it never persists a
/// conversation), so the shared builder cannot depend on Track B's aggregate.
/// </summary>
public sealed record AgentTurn(bool FromAssistant, string Content);
