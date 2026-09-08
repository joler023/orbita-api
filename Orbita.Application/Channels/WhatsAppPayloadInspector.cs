using System.Text.Json;

namespace Orbita.Application.Channels;

/// <summary>
/// Pulls just the routing id out of a raw WhatsApp webhook payload with a forward-only
/// <see cref="Utf8JsonReader"/> scan — no full deserialization — so ingestion can
/// resolve a <see cref="Domain.Channels.ChannelAccount"/> before trusting the payload
/// shape at all (ORB-B02; ORB-B03 does the real parsing).
/// </summary>
public static class WhatsAppPayloadInspector
{
    /// <summary>Meta's phone_number_id, nested under entry[].changes[].value.metadata.phone_number_id.</summary>
    public static string? ReadPhoneNumberId(ReadOnlySpan<byte> rawBody)
    {
        try
        {
            var reader = new Utf8JsonReader(rawBody);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("phone_number_id"u8))
                {
                    return reader.Read() && reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
