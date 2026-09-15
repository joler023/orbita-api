using System.Text.Json;
using Orbita.Application.Channels;
using Orbita.Domain.Channels;
using Orbita.Domain.Inbox;

namespace Orbita.Infrastructure.Channels;

/// <summary>
/// Turns a raw WhatsApp Cloud API webhook payload into <see cref="InboundItem"/>s
/// (ORB-B03). Unlike <see cref="WhatsAppPayloadInspector"/> (a cheap routing-only scan
/// used before the payload is trusted), this does a full parse — the payload has
/// already been routed to a known <see cref="Domain.Channels.ChannelAccount"/> by then.
/// An unrecognized message type is never dropped: its body becomes
/// "[tipo no soportado]" rather than being silently lost.
/// </summary>
public static class WhatsAppPayloadParser
{
    private const string UnsupportedTypeBody = "[tipo no soportado]";

    public static IReadOnlyList<InboundItem> Parse(string payloadJson)
    {
        var items = new List<InboundItem>();
        using var document = JsonDocument.Parse(payloadJson);

        if (!document.RootElement.TryGetProperty("entry", out var entries))
        {
            return items;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes))
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (change.TryGetProperty("value", out var value))
                {
                    ParseChangeValue(value, items);
                }
            }
        }

        return items;
    }

    private static void ParseChangeValue(JsonElement value, List<InboundItem> items)
    {
        var accountExternalId = value.TryGetProperty("metadata", out var metadata)
            && metadata.TryGetProperty("phone_number_id", out var phoneNumberId)
            ? phoneNumberId.GetString() ?? string.Empty
            : string.Empty;

        var contactNames = ReadContactNames(value);

        if (value.TryGetProperty("messages", out var messages))
        {
            foreach (var messageEl in messages.EnumerateArray())
            {
                items.Add(ParseMessage(messageEl, accountExternalId, contactNames));
            }
        }

        if (value.TryGetProperty("statuses", out var statuses))
        {
            foreach (var statusEl in statuses.EnumerateArray())
            {
                items.Add(ParseStatus(statusEl, accountExternalId));
            }
        }
    }

    private static Dictionary<string, string> ReadContactNames(JsonElement value)
    {
        var names = new Dictionary<string, string>();
        if (!value.TryGetProperty("contacts", out var contacts))
        {
            return names;
        }

        foreach (var contact in contacts.EnumerateArray())
        {
            if (contact.TryGetProperty("wa_id", out var waId)
                && contact.TryGetProperty("profile", out var profile)
                && profile.TryGetProperty("name", out var name))
            {
                names[waId.GetString() ?? string.Empty] = name.GetString() ?? string.Empty;
            }
        }

        return names;
    }

    private static InboundMessage ParseMessage(JsonElement messageEl, string accountExternalId, IReadOnlyDictionary<string, string> contactNames)
    {
        var from = messageEl.TryGetProperty("from", out var fromEl) ? fromEl.GetString() ?? string.Empty : string.Empty;
        var externalId = messageEl.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
        var replyTo = messageEl.TryGetProperty("context", out var context) && context.TryGetProperty("id", out var contextId)
            ? contextId.GetString()
            : null;
        contactNames.TryGetValue(from, out var displayName);

        var type = messageEl.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        var (body, mediaExternalId, mediaMime) = ExtractContent(messageEl, type);

        return new InboundMessage(
            accountExternalId, externalId, from, displayName, ChannelKind.WhatsApp,
            body, mediaExternalId, mediaMime, replyTo, ParseTimestamp(messageEl));
    }

    private static InboundStatusUpdate ParseStatus(JsonElement statusEl, string accountExternalId)
    {
        var externalId = statusEl.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
        var statusText = statusEl.TryGetProperty("status", out var statusTextEl) ? statusTextEl.GetString() : null;

        string? errorCode = null;
        if (statusEl.TryGetProperty("errors", out var errors))
        {
            foreach (var error in errors.EnumerateArray())
            {
                if (error.TryGetProperty("code", out var code))
                {
                    errorCode = code.ToString();
                }

                break;
            }
        }

        var status = statusText switch
        {
            "sent" => MessageStatus.Sent,
            "delivered" => MessageStatus.Delivered,
            "read" => MessageStatus.Read,
            "failed" => MessageStatus.Failed,
            _ => MessageStatus.Sent,
        };

        return new InboundStatusUpdate(accountExternalId, externalId, ChannelKind.WhatsApp, status, errorCode, ParseTimestamp(statusEl));
    }

    private static (string? Body, string? MediaExternalId, string? MediaMime) ExtractContent(JsonElement messageEl, string? type)
        => type switch
        {
            "text" => (messageEl.TryGetProperty("text", out var text) && text.TryGetProperty("body", out var body) ? body.GetString() : null, null, null),
            "image" => (GetMediaField(messageEl, "image", "caption"), GetMediaField(messageEl, "image", "id"), GetMediaField(messageEl, "image", "mime_type")),
            "audio" => (null, GetMediaField(messageEl, "audio", "id"), GetMediaField(messageEl, "audio", "mime_type")),
            "video" => (GetMediaField(messageEl, "video", "caption"), GetMediaField(messageEl, "video", "id"), GetMediaField(messageEl, "video", "mime_type")),
            "document" => (GetMediaField(messageEl, "document", "caption"), GetMediaField(messageEl, "document", "id"), GetMediaField(messageEl, "document", "mime_type")),
            "sticker" => (null, GetMediaField(messageEl, "sticker", "id"), GetMediaField(messageEl, "sticker", "mime_type")),
            "location" => (FormatLocation(messageEl), null, null),
            "contacts" => ("[contacto compartido]", null, null),
            "reaction" => (GetMediaField(messageEl, "reaction", "emoji"), null, null),
            "button" => (GetMediaField(messageEl, "button", "text"), null, null),
            "interactive" => (ExtractInteractive(messageEl), null, null),
            _ => (UnsupportedTypeBody, null, null),
        };

    private static string? GetMediaField(JsonElement messageEl, string typeKey, string field)
        => messageEl.TryGetProperty(typeKey, out var typeEl) && typeEl.TryGetProperty(field, out var value)
            ? value.GetString()
            : null;

    private static string? FormatLocation(JsonElement messageEl)
    {
        if (!messageEl.TryGetProperty("location", out var location)
            || !location.TryGetProperty("latitude", out var latitude)
            || !location.TryGetProperty("longitude", out var longitude))
        {
            return null;
        }

        return $"[ubicación] {latitude.GetDouble()}, {longitude.GetDouble()}";
    }

    private static string ExtractInteractive(JsonElement messageEl)
    {
        if (!messageEl.TryGetProperty("interactive", out var interactive)
            || !interactive.TryGetProperty("type", out var interactiveType))
        {
            return UnsupportedTypeBody;
        }

        var key = interactiveType.GetString();
        if (key is "button_reply" or "list_reply"
            && interactive.TryGetProperty(key, out var reply)
            && reply.TryGetProperty("title", out var title))
        {
            return title.GetString() ?? UnsupportedTypeBody;
        }

        return UnsupportedTypeBody;
    }

    private static DateTimeOffset ParseTimestamp(JsonElement element)
        => element.TryGetProperty("timestamp", out var timestamp)
            && long.TryParse(timestamp.GetString(), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow;
}
