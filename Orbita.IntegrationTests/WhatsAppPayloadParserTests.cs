using Orbita.Application.Channels;
using Orbita.Domain.Channels;
using Orbita.Domain.Inbox;
using Orbita.Infrastructure.Channels;

namespace Orbita.IntegrationTests;

/// <summary>No database involved — lives here rather than Orbita.UnitTests only because Orbita.UnitTests doesn't reference Orbita.Infrastructure.</summary>
public sealed class WhatsAppPayloadParserTests
{
    [Fact]
    public void Parse_TextMessage_ReturnsInboundMessageWithSenderNameFromContacts()
    {
        var items = WhatsAppPayloadParser.Parse(Payload("""
            "contacts": [{ "profile": { "name": "Ana Pérez" }, "wa_id": "573001234567" }],
            "messages": [{
                "from": "573001234567", "id": "wamid.1", "timestamp": "1690000000", "type": "text",
                "text": { "body": "hola" }
            }]
            """));

        var message = Assert.IsType<InboundMessage>(Assert.Single(items));
        Assert.Equal("phone-1", message.AccountExternalId);
        Assert.Equal("wamid.1", message.ExternalId);
        Assert.Equal("573001234567", message.SenderExternalId);
        Assert.Equal("Ana Pérez", message.SenderDisplayName);
        Assert.Equal(ChannelKind.WhatsApp, message.Kind);
        Assert.Equal("hola", message.Body);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1690000000), message.Timestamp);
    }

    [Fact]
    public void Parse_MessageWithReplyContext_KeepsTheReferencedExternalId()
    {
        var items = WhatsAppPayloadParser.Parse(Payload("""
            "messages": [{
                "from": "573001234567", "id": "wamid.2", "timestamp": "1690000000", "type": "text",
                "text": { "body": "sí" }, "context": { "id": "wamid.1" }
            }]
            """));

        var message = Assert.IsType<InboundMessage>(Assert.Single(items));
        Assert.Equal("wamid.1", message.ReplyToExternalId);
    }

    [Fact]
    public void Parse_ImageMessage_ExtractsMediaIdMimeAndCaption()
    {
        var items = WhatsAppPayloadParser.Parse(Payload("""
            "messages": [{
                "from": "573001234567", "id": "wamid.3", "timestamp": "1690000000", "type": "image",
                "image": { "id": "media-1", "mime_type": "image/jpeg", "caption": "mira esto" }
            }]
            """));

        var message = Assert.IsType<InboundMessage>(Assert.Single(items));
        Assert.Equal("mira esto", message.Body);
        Assert.Equal("media-1", message.MediaExternalId);
        Assert.Equal("image/jpeg", message.MediaMime);
    }

    [Fact]
    public void Parse_UnsupportedMessageType_NeverDropsTheMessage()
    {
        var items = WhatsAppPayloadParser.Parse(Payload("""
            "messages": [{ "from": "573001234567", "id": "wamid.4", "timestamp": "1690000000", "type": "order" }]
            """));

        var message = Assert.IsType<InboundMessage>(Assert.Single(items));
        Assert.Equal("[tipo no soportado]", message.Body);
    }

    [Fact]
    public void Parse_StatusUpdate_ReturnsInboundStatusUpdateWithErrorCode()
    {
        var items = WhatsAppPayloadParser.Parse(Payload("""
            "statuses": [{
                "id": "wamid.1", "status": "failed", "timestamp": "1690000000",
                "errors": [{ "code": 131047 }]
            }]
            """));

        var status = Assert.IsType<InboundStatusUpdate>(Assert.Single(items));
        Assert.Equal("wamid.1", status.ExternalId);
        Assert.Equal(MessageStatus.Failed, status.Status);
        Assert.Equal("131047", status.ErrorCode);
    }

    [Fact]
    public void Parse_PayloadWithNoEntry_ReturnsEmpty()
        => Assert.Empty(WhatsAppPayloadParser.Parse("""{"object":"whatsapp_business_account"}"""));

    private static string Payload(string valueBody)
        => $$"""
            {
              "object": "whatsapp_business_account",
              "entry": [{
                "id": "waba-1",
                "changes": [{
                  "value": {
                    "messaging_product": "whatsapp",
                    "metadata": { "display_phone_number": "573001112233", "phone_number_id": "phone-1" },
                    {{valueBody}}
                  },
                  "field": "messages"
                }]
              }]
            }
            """;
}
