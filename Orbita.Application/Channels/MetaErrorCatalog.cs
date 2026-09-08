namespace Orbita.Application.Channels;

/// <summary>
/// Translates Meta's numeric <c>error.code</c> into (is this worth retrying, what do we
/// tell the agent) at read/dispatch time — no new column, no stored translation (ORB-B05).
/// </summary>
public static class MetaErrorCatalog
{
    public static (bool IsTransient, string MessageEs) Describe(string code) => code switch
    {
        "131047" => (false, "Han pasado más de 24 horas desde el último mensaje del cliente: usa una plantilla aprobada."),
        "131026" => (false, "El destinatario no tiene WhatsApp o no puede recibir mensajes."),
        "130429" or "131056" => (true, "Se alcanzó el límite de envío de la cuenta; se reintentará automáticamente."),
        "100" => (false, "Uno de los parámetros del mensaje es inválido."),
        "190" or "131001" => (false, "El token de acceso del canal expiró; hay que reconectarlo."),
        "133010" => (false, "La cuenta de WhatsApp Business no está registrada correctamente."),
        "470" or "131053" => (false, "Hubo un problema con el archivo multimedia enviado."),
        _ => (false, "Meta rechazó el mensaje."),
    };
}
