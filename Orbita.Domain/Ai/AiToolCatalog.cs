using System.Collections.Frozen;

namespace Orbita.Domain.Ai;

/// <summary>
/// Every action an assistant can be given, and whether it works yet — ORB-C05's typed
/// tools, published so ORB-C10's configuration screen can list them.
///
/// A catalog rather than a hardcoded list in the frontend, for the reason the frontend
/// argued and I agree with: hardcoding forces a frontend release every time a tool is
/// added, and several of these depend on modules that do not exist yet. Shipping
/// <see cref="AiTool.IsAvailable"/> lets the UI show a tool as coming rather than pretend
/// it is absent.
///
/// It is product-level, not tenant-level: every organization sees the same catalog. What
/// varies per agent is which of them are *enabled*, which lives on <see cref="AiAgent"/>.
/// </summary>
public static class AiToolCatalog
{
    /// <summary>Searches the agent's own documents. The only one that works today (ORB-C03).</summary>
    public const string ConsultarConocimiento = "consultar_conocimiento";

    public const string CrearOportunidad = "crear_oportunidad";

    public const string MoverEtapa = "mover_etapa";

    public const string AgendarCita = "agendar_cita";

    public const string EscalarAHumano = "escalar_a_humano";

    private static readonly FrozenDictionary<string, AiTool> ByKey = new AiTool[]
    {
        new(
            ConsultarConocimiento,
            "Consultar los documentos del negocio",
            "Busca la respuesta en los documentos que subiste antes de contestar.",
            IsAvailable: true,
            UnavailableReason: null),
        // Available since ORB-C05. Both act only on the contact of the conversation the
        // assistant is in, never on an id the model supplies — see IOpportunityService.
        new(
            CrearOportunidad,
            "Registrar una oportunidad de venta",
            "Crea una oportunidad en el tablero cuando detecta interés de compra.",
            IsAvailable: true,
            UnavailableReason: null),
        new(
            MoverEtapa,
            "Mover una oportunidad de etapa",
            "Avanza una oportunidad en el tablero cuando la conversación lo justifica.",
            IsAvailable: true,
            UnavailableReason: null),
        new(
            AgendarCita,
            "Agendar una cita",
            "Reserva un espacio en la agenda cuando el cliente lo pide.",
            IsAvailable: false,
            "Disponible cuando se active la agenda."),
        new(
            EscalarAHumano,
            "Pasar la conversación a una persona",
            "Entrega la conversación al equipo cuando no puede resolver o el cliente lo pide.",
            IsAvailable: false,
            "Disponible cuando se active la bandeja de conversaciones."),
    }.ToFrozenDictionary(tool => tool.Key);

    public static IReadOnlyList<AiTool> All { get; } = [.. ByKey.Values];

    public static bool Exists(string key) => ByKey.ContainsKey(key);

    /// <summary>
    /// Whether a tool can actually run right now. Enabling an unavailable one is rejected
    /// rather than silently accepted, so nobody configures an assistant to do something
    /// that will quietly never happen.
    /// </summary>
    public static bool IsAvailable(string key) => ByKey.TryGetValue(key, out var tool) && tool.IsAvailable;
}

/// <param name="Key">The identifier the model uses when calling it, and what gets stored on the agent.</param>
/// <param name="DisplayName">Shown in the configuration screen. Deliberately free of jargon.</param>
/// <param name="Description">One sentence explaining what the assistant would do, in the owner's language.</param>
/// <param name="UnavailableReason">Why it cannot be enabled yet. Null when it can.</param>
public sealed record AiTool(
    string Key,
    string DisplayName,
    string Description,
    bool IsAvailable,
    string? UnavailableReason);
