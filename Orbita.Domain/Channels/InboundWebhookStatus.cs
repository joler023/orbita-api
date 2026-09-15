namespace Orbita.Domain.Channels;

/// <summary>Lifecycle of one row in the inbound webhook queue (ORB-B02).</summary>
public enum InboundWebhookStatus
{
    Pending,
    Processing,
    Processed,
    Dead,
}
