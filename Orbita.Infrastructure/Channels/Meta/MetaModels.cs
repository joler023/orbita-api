using System.Text.Json.Serialization;

namespace Orbita.Infrastructure.Channels.Meta;

/// <summary>Graph API's error envelope — present (with a non-2xx status) whenever a call fails.</summary>
internal sealed record MetaErrorEnvelope([property: JsonPropertyName("error")] MetaError? Error);

internal sealed record MetaError(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("code")] int? Code,
    [property: JsonPropertyName("error_subcode")] int? ErrorSubcode);

/// <summary>GET /oauth/access_token.</summary>
internal sealed record MetaTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("expires_in")] long? ExpiresInSeconds);

/// <summary>GET /debug_token.</summary>
internal sealed record MetaDebugTokenResponse([property: JsonPropertyName("data")] MetaDebugTokenData Data);

/// <param name="ExpiresAtUnixSeconds">0 means the token never expires.</param>
internal sealed record MetaDebugTokenData(
    [property: JsonPropertyName("is_valid")] bool IsValid,
    [property: JsonPropertyName("expires_at")] long? ExpiresAtUnixSeconds);

/// <summary>GET /{phone_number_id}?fields=display_phone_number,verified_name.</summary>
internal sealed record WhatsAppPhoneNumberResponse(
    [property: JsonPropertyName("display_phone_number")] string DisplayPhoneNumber,
    [property: JsonPropertyName("verified_name")] string VerifiedName);

/// <summary>POST /{waba_id}/subscribed_apps.</summary>
internal sealed record MetaSuccessResponse([property: JsonPropertyName("success")] bool Success);

/// <summary>POST /{phone_number_id}/messages.</summary>
internal sealed record WhatsAppSendMessageResponse([property: JsonPropertyName("messages")] IReadOnlyList<WhatsAppSentMessageId> Messages);

internal sealed record WhatsAppSentMessageId([property: JsonPropertyName("id")] string Id);
