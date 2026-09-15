namespace Orbita.Application.Channels;

/// <summary>Meta's webhook verification handshake carried an unknown mode or a wrong verify token.</summary>
public sealed class WebhookVerificationFailedException() : Exception("Webhook verification failed.");
