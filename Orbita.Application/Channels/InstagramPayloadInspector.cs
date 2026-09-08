using System.Text.Json;

namespace Orbita.Application.Channels;

/// <summary>
/// Pulls the routing id out of a raw Instagram webhook payload (ORB-B02): the first
/// <c>entry[].id</c> is the IG business account id Meta is notifying on behalf of — not
/// a username, and not the sender.
/// </summary>
public static class InstagramPayloadInspector
{
    public static string? ReadAccountId(ReadOnlySpan<byte> rawBody)
    {
        try
        {
            var reader = new Utf8JsonReader(rawBody);
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("entry"u8))
                {
                    continue;
                }

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray
                    || !reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                {
                    return null;
                }

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("id"u8))
                    {
                        return reader.Read() && reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    }

                    reader.Skip();
                }

                return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
