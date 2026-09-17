using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orbita.Application.Crm;
using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// ORB-C05: the typed tools an assistant can call, and the only place their arguments are
/// read, validated and turned into a real action.
/// </summary>
public interface IAgentToolExecutor
{
    /// <summary>
    /// The tools this assistant may call, as the model sees them. Only those enabled on
    /// the agent <em>and</em> executable here — an enabled tool the executor cannot run
    /// would be offered to the model and then fail on every call.
    /// </summary>
    IReadOnlyList<LlmTool> DefinitionsFor(AiAgent agent);

    /// <summary>
    /// Runs one call. Never throws for a tool that fails: the result says so, in words
    /// the model can relay, because "una herramienta que falla no rompe la conversación"
    /// is an acceptance criterion and an exception here would end the reply.
    /// </summary>
    Task<AgentToolResult> ExecuteAsync(AgentToolContext context, LlmToolCall call, CancellationToken cancellationToken);
}

/// <summary>
/// Everything a tool is allowed to act on, fixed by the conversation and never by the
/// model: the tenant it runs in, the assistant acting, and the customer being served.
/// </summary>
public sealed record AgentToolContext(Guid TenantId, Guid AgentId, Guid ContactId, Guid ConversationId);

/// <param name="ResultJson">What goes back to the model as the tool's answer.</param>
public sealed record AgentToolResult(string ToolName, bool Succeeded, string ResultJson);

public sealed class AgentToolExecutor(
    IOpportunityService opportunities,
    Orbita.Application.Inbox.IConversationHandoffService handoffService,
    ILogger<AgentToolExecutor> logger) : IAgentToolExecutor
{
    public const int TitleMaxLength = 200;

    /// <summary>Same bound as the column the note lands in (<c>Conversation.HandoffSummaryMaxLength</c>).</summary>
    public const int SummaryMaxLength = 600;

    private static readonly FrozenTools Tools = new();

    public IReadOnlyList<LlmTool> DefinitionsFor(AiAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return [.. agent.Tools.Select(Tools.Find).OfType<LlmTool>()];
    }

    public async Task<AgentToolResult> ExecuteAsync(AgentToolContext context, LlmToolCall call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(call);

        try
        {
            using var arguments = ParseArguments(call.ArgumentsJson);

            return call.Name switch
            {
                AiToolCatalog.CrearOportunidad => await CreateOpportunityAsync(context, arguments.RootElement, cancellationToken),
                AiToolCatalog.MoverEtapa => await MoveStageAsync(context, arguments.RootElement, cancellationToken),
                AiToolCatalog.EscalarAHumano => await HandOffAsync(context, arguments.RootElement, cancellationToken),
                _ => Failure(call.Name, "Esa herramienta no está disponible para este asistente."),
            };
        }
        catch (ToolArgumentException exception)
        {
            return Failure(call.Name, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A broken tool is that tool's problem. The customer still gets an answer; the
            // model is told plainly that the action did not happen, so it does not claim
            // otherwise.
            logger.LogWarning(exception, "Tool {Tool} failed on conversation {ConversationId}.", call.Name, context.ConversationId);

            return Failure(call.Name, "No se pudo completar la acción en este momento.");
        }
    }

    private async Task<AgentToolResult> CreateOpportunityAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var title = RequireString(arguments, "titulo", TitleMaxLength);
        var amount = OptionalAmount(arguments, "monto");

        // Contact, pipeline and tenant come from the context. Even if the model sends an
        // opportunity or contact id of its own, nothing here reads one.
        var created = await opportunities.CreateForAgentAsync(
            context.TenantId, context.AgentId, context.ContactId, title, amount, cancellationToken);

        return Success(AiToolCatalog.CrearOportunidad, new { ok = true, oportunidad = created.Title, etapa = created.StageId });
    }

    private async Task<AgentToolResult> MoveStageAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var stageName = RequireString(arguments, "etapa", 120);

        try
        {
            var moved = await opportunities.MoveContactOpportunityForAgentAsync(
                context.TenantId, context.AgentId, context.ContactId, stageName, cancellationToken);

            return moved is null
                ? Failure(AiToolCatalog.MoverEtapa, "Este cliente no tiene una oportunidad abierta para mover.")
                : Success(AiToolCatalog.MoverEtapa, new { ok = true, oportunidad = moved.Title, etapa = stageName });
        }
        catch (StageNotFoundException)
        {
            return Failure(AiToolCatalog.MoverEtapa, $"No existe una etapa llamada \"{stageName}\" en el tablero.");
        }
    }

    /// <summary>
    /// ORB-C07's fourth trigger: the assistant's own judgment. The others are read off the
    /// customer's words before any model call; this one is the model deciding it cannot
    /// help, which is the half a phrase list will never catch.
    ///
    /// The note is optional and, when it comes, is the model's own — it is the only party
    /// here holding the whole exchange, and taking it saves the cheap summarizing call the
    /// other paths pay for. The reply it writes afterwards still goes out: that is the
    /// assistant saying goodbye in the customer's own language, which a stored sentence
    /// cannot do.
    /// </summary>
    private async Task<AgentToolResult> HandOffAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var summary = OptionalString(arguments, "resumen", SummaryMaxLength);

        var handedOver = await handoffService.RequestAsync(
            context.TenantId,
            context.ConversationId,
            context.AgentId,
            Domain.Inbox.HandoffReason.AgentDecision,
            summary,
            cancellationToken);

        // Already queued is a success from the model's side: the customer is waiting for a
        // person either way, and telling it otherwise would make it try again or apologize
        // for something that did happen.
        return Success(
            AiToolCatalog.EscalarAHumano,
            new { ok = true, traspasada = true, yaEstaba = !handedOver });
    }

    private static JsonDocument ParseArguments(string json)
    {
        try
        {
            var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new ToolArgumentException("Los argumentos de la herramienta no son válidos.");
            }

            return document;
        }
        catch (JsonException)
        {
            throw new ToolArgumentException("Los argumentos de la herramienta no son válidos.");
        }
    }

    private static string RequireString(JsonElement arguments, string name, int maxLength)
    {
        if (!arguments.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ToolArgumentException($"Falta el dato \"{name}\".");
        }

        var text = value.GetString()!.Trim();

        return text.Length > maxLength
            ? throw new ToolArgumentException($"El dato \"{name}\" es demasiado largo.")
            : text;
    }

    private static string? OptionalString(JsonElement arguments, string name, int maxLength)
    {
        if (!arguments.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            return null;
        }

        var text = value.GetString()!.Trim();

        return text.Length > maxLength ? text[..maxLength] : text;
    }

    private static decimal? OptionalAmount(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var amount = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw new ToolArgumentException($"El dato \"{name}\" debe ser un número."),
        };

        return amount < 0 ? throw new ToolArgumentException($"El dato \"{name}\" no puede ser negativo.") : amount;
    }

    private static AgentToolResult Success(string tool, object payload)
        => new(tool, true, JsonSerializer.Serialize(payload));

    private static AgentToolResult Failure(string tool, string reason)
        => new(tool, false, JsonSerializer.Serialize(new { ok = false, motivo = reason }));

    private sealed class ToolArgumentException(string message) : Exception(message);

    /// <summary>The JSON schemas the model is shown. Spanish descriptions, like the product.</summary>
    private sealed class FrozenTools
    {
        private readonly Dictionary<string, LlmTool> _byKey = new(StringComparer.Ordinal)
        {
            [AiToolCatalog.CrearOportunidad] = new(
                AiToolCatalog.CrearOportunidad,
                "Registra una oportunidad de venta para el cliente de esta conversación cuando muestra "
                    + "intención clara de comprar. No la uses para consultas generales.",
                """
                {
                  "type": "object",
                  "properties": {
                    "titulo": { "type": "string", "description": "Qué quiere comprar el cliente, en pocas palabras.", "maxLength": 200 },
                    "monto": { "type": "number", "minimum": 0, "description": "Valor estimado, solo si el cliente lo dijo." }
                  },
                  "required": ["titulo"],
                  "additionalProperties": false
                }
                """),
            [AiToolCatalog.MoverEtapa] = new(
                AiToolCatalog.MoverEtapa,
                "Mueve la oportunidad abierta del cliente de esta conversación a otra etapa del tablero "
                    + "cuando la conversación lo justifica (por ejemplo, ya pidió cotización).",
                """
                {
                  "type": "object",
                  "properties": {
                    "etapa": { "type": "string", "description": "Nombre exacto de la etapa destino, como aparece en el tablero." }
                  },
                  "required": ["etapa"],
                  "additionalProperties": false
                }
                """),
            [AiToolCatalog.EscalarAHumano] = new(
                AiToolCatalog.EscalarAHumano,
                "Entrega esta conversación a una persona del equipo cuando el cliente lo pide, cuando "
                    + "no puedes resolver lo que necesita, o cuando se está frustrando. Después de usarla, "
                    + "despídete brevemente: tú ya no vas a seguir respondiendo en esta conversación.",
                """
                {
                  "type": "object",
                  "properties": {
                    "resumen": { "type": "string", "description": "Qué necesita el cliente y en qué quedó la conversación, en dos o tres frases, para quien la retome.", "maxLength": 600 }
                  },
                  "required": [],
                  "additionalProperties": false
                }
                """),
        };

        public LlmTool? Find(string key) => _byKey.GetValueOrDefault(key);
    }
}
