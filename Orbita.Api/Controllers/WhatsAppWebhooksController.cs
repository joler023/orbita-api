using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Channels;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-B01: Meta's webhook verification handshake for WhatsApp. Anonymous by necessity
/// (Meta is the caller); the verify token is what proves the request is Meta's. Two
/// routes feed the same service: the app-level callback configured once in Meta's
/// dashboard (global verify token), and the per-account callback registered through
/// <c>override_callback_uri</c> at connect time (the account's own webhook secret).
/// Inbound message POSTs land on these same routes in ORB-B02.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class WhatsAppWebhooksController(IChannelWebhookVerificationService verificationService) : ControllerBase
{
    [HttpGet("api/webhooks/whatsapp")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ContentResult> VerifyAppCallback(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        CancellationToken cancellationToken)
        => VerifyAsync(null, mode, verifyToken, challenge, cancellationToken);

    [HttpGet("api/webhooks/whatsapp/{channelAccountId:guid}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ContentResult> VerifyAccountCallback(
        Guid channelAccountId,
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        CancellationToken cancellationToken)
        => VerifyAsync(channelAccountId, mode, verifyToken, challenge, cancellationToken);

    private async Task<ContentResult> VerifyAsync(Guid? channelAccountId, string? mode, string? verifyToken, string? challenge, CancellationToken cancellationToken)
    {
        // Meta expects the raw hub.challenge echoed back as the plain-text body.
        var echoed = await verificationService.VerifyWhatsAppAsync(channelAccountId, mode, verifyToken, challenge, cancellationToken);
        return Content(echoed, "text/plain");
    }
}
