namespace Orbita.Application.Ai;

/// <summary>
/// An assistant that has already worked cannot be deleted — it can be switched off.
///
/// Its <c>ai_runs</c> are the consumption ledger ORB-A13's metering and ORB-A12's billing
/// read, and its replies are part of real customer conversations. Deleting the assistant
/// would either destroy that history or leave it pointing at nothing, and neither is a
/// reasonable outcome of clicking a button on a configuration screen.
///
/// Deleting stays available for the case it actually serves: an assistant created by
/// mistake, that never answered anyone. For every other case ORB-C10 already shipped the
/// right verb — <c>PATCH .../enabled</c>, "activar y desactivar el agente sin borrarlo".
/// </summary>
public sealed class AgentHasHistoryException()
    : InvalidOperationException(
        "Este asistente ya atendió conversaciones, así que no se puede eliminar. "
        + "Desactivalo si querés que deje de responder.");
