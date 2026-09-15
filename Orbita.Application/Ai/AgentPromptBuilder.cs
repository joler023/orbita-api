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
    /// message that proves it.
    /// </summary>
    public const string ConversationRules =
        "Estás respondiendo por chat a un cliente real del negocio.\n"
        + "- Respondé en el mismo idioma en que te escribió el cliente.\n"
        + "- Usá solo la información de los documentos del negocio y de esta conversación. "
        + "No inventes precios, horarios, plazos ni políticas.\n"
        + "- Si no tenés la información para responder, decilo con naturalidad y ofrecé "
        + "pasar la conversación con una persona del equipo.\n"
        + "- Escribí como en un chat: breve y directo, sin encabezados ni listas largas.";

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
