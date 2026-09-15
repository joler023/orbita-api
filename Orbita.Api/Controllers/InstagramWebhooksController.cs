using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Channels;
using Orbita.Domain.Channels;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-B02: Meta's webhook verification handshake and inbound ingestion for Instagram.
/// Only the app-level route exists — there is no Instagram adapter or per-account
/// callback registration until ORB-B09, so every payload is routed from
/// <c>entry[].id</c> alone via <see cref="IWebhookIngestionService"/>.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class InstagramWebhooksController(
    IChannelWebhookVerificationService verificationService,
    IWebhookIngestionService ingestionService) : ControllerBase
{
    [HttpGet("api/webhooks/instagram")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ContentResult> Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        CancellationToken cancellationToken)
    {
        // Same app-level verify token as WhatsApp — one Meta app covers both products;
        // channelAccountId is always null here, so this is exactly the global-token check.
        var echoed = await verificationService.VerifyWhatsAppAsync(null, mode, verifyToken, challenge, cancellationToken);
        return Content(echoed, "text/plain");
    }

    [HttpPost("api/webhooks/instagram")]
    [RequestSizeLimit(1_048_576)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var rawBody = buffer.ToArray();
        var signatureHeader = Request.Headers["X-Hub-Signature-256"].ToString();

        await ingestionService.IngestAsync(ChannelKind.Instagram, null, rawBody, signatureHeader, cancellationToken);
        return Ok();
    }
}
