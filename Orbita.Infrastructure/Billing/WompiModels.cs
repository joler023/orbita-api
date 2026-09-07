using System.Text.Json.Serialization;

namespace Orbita.Infrastructure.Billing;

// Minimal DTOs for the slice of Wompi's REST API WompiPaymentProvider actually calls —
// not a full client. See https://docs.wompi.co for the complete API.

internal sealed class WompiEnvelope<T>
{
    [JsonPropertyName("data")]
    public T Data { get; set; } = default!;
}

internal sealed class WompiPaymentSource
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}

internal sealed class WompiTransaction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

internal sealed class WompiMerchant
{
    [JsonPropertyName("presigned_acceptance")]
    public WompiPresignedAcceptance PresignedAcceptance { get; set; } = default!;
}

internal sealed class WompiPresignedAcceptance
{
    [JsonPropertyName("acceptance_token")]
    public string AcceptanceToken { get; set; } = string.Empty;
}
