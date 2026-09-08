namespace Orbita.Application.Channels;

/// <summary>
/// Verifies a Meta webhook's <c>X-Hub-Signature-256</c> header against the raw request
/// body (ORB-B02). Throws <see cref="Billing.InvalidWebhookSignatureException"/> on
/// failure — the same exception Stripe/Wompi webhook verification already uses: one
/// concept ("this webhook's signature doesn't check out"), one type, one HTTP status.
/// </summary>
public interface IWebhookSignatureVerifier
{
    /// <exception cref="Billing.InvalidWebhookSignatureException">The header is missing, malformed, or doesn't match.</exception>
    void Verify(byte[] rawBody, string? signatureHeader);
}
