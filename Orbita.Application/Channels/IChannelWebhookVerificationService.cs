namespace Orbita.Application.Channels;

/// <summary>
/// Answers Meta's webhook verification handshake (GET with hub.mode/hub.verify_token/
/// hub.challenge) for both the app-level callback and the per-account one (ORB-B01).
/// </summary>
public interface IChannelWebhookVerificationService
{
    /// <param name="channelAccountId">Null for the app-level callback, whose verify token is platform configuration.</param>
    /// <returns>The hub.challenge to echo back as plain text.</returns>
    /// <exception cref="WebhookVerificationFailedException">Wrong mode, wrong token, or unknown account.</exception>
    Task<string> VerifyWhatsAppAsync(Guid? channelAccountId, string? mode, string? verifyToken, string? challenge, CancellationToken cancellationToken);
}
