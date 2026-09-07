namespace Orbita.Application.Billing;

/// <summary>The payload's signature doesn't match what the provider's shared secret would produce.</summary>
public sealed class InvalidWebhookSignatureException() : Exception("Webhook signature verification failed.");
