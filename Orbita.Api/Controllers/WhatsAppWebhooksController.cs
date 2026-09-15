using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Channels;
using Orbita.Domain.Channels;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-B01/ORB-B02: Meta's webhook verification handshake plus inbound message
/// ingestion for WhatsApp. Anonymous by necessity (Meta is the caller). Two routes feed
/// the same services: the app-level callback configured once in Meta's dashboard
/// (global verify token), and the per-account callback registered through
/// <c>override_callback_uri</c> at connect time (the account's own webhook secret).
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class WhatsAppWebhooksController(
    IChannelWebhookVerificationService verificationService,
    IWebhookIngestionService ingestionService) : ControllerBase
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

    /// <summary>
    /// Meta's app-level message delivery route (used when no per-account callback
    /// override was registered, e.g. local development with no public URL yet).
    /// </summary>
    [HttpPost("api/webhooks/whatsapp")]
    [RequestSizeLimit(1_048_576)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> ReceiveAppCallback(CancellationToken cancellationToken)
        => ReceiveAsync(null, cancellationToken);

    [HttpPost("api/webhooks/whatsapp/{channelAccountId:guid}")]
    [RequestSizeLimit(1_048_576)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> ReceiveAccountCallback(Guid channelAccountId, CancellationToken cancellationToken)
        => ReceiveAsync(channelAccountId, cancellationToken);

    private async Task<ContentResult> VerifyAsync(Guid? channelAccountId, string? mode, string? verifyToken, string? challenge, CancellationToken cancellationToken)
    {
        // Meta expects the raw hub.challenge echoed back as the plain-text body.
        var echoed = await verificationService.VerifyWhatsAppAsync(channelAccountId, mode, verifyToken, challenge, cancellationToken);
        return Content(echoed, "text/plain");
    }

    private async Task<IActionResult> ReceiveAsync(Guid? channelAccountId, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var rawBody = buffer.ToArray();
        var signatureHeader = Request.Headers["X-Hub-Signature-256"].ToString();

        // Unroutable/duplicate still answer 200: Meta retries forever on anything else,
        // and neither case is something a redelivery would fix. An invalid signature is
        // the one failure that surfaces (InvalidWebhookSignatureException -> 401).
        await ingestionService.IngestAsync(ChannelKind.WhatsApp, channelAccountId, rawBody, signatureHeader, cancellationToken);
        return Ok();
    }
}
