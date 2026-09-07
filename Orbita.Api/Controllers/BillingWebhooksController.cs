using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Billing;
using DomainPaymentProvider = Orbita.Domain.Billing.PaymentProvider;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-A12: where Stripe/Wompi tell us a subscription's status changed. Anonymous by
/// necessity (the provider calls this, not a logged-in user) — the payload's own
/// signature is what proves authenticity, verified inside ISubscriptionWebhookService.
/// Both routes read the raw request body instead of binding a DTO because signature
/// verification needs the exact bytes the provider signed, not a re-serialized copy.
/// </summary>
[ApiController]
[Route("api/billing/webhooks")]
[AllowAnonymous]
public sealed class BillingWebhooksController(ISubscriptionWebhookService webhookService) : ControllerBase
{
    [HttpPost("stripe")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Stripe(CancellationToken cancellationToken)
    {
        var payload = await ReadBodyAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();
        await webhookService.HandleWebhookAsync(DomainPaymentProvider.Stripe, payload, signature, cancellationToken);
        return Ok();
    }

    [HttpPost("wompi")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Wompi(CancellationToken cancellationToken)
    {
        var payload = await ReadBodyAsync(cancellationToken);
        // Wompi embeds its checksum inside the JSON body itself rather than an HTTP
        // header — WompiPaymentProvider.ParseWebhookEvent ignores this parameter.
        await webhookService.HandleWebhookAsync(DomainPaymentProvider.Wompi, payload, signatureHeader: string.Empty, cancellationToken);
        return Ok();
    }

    private async Task<string> ReadBodyAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
