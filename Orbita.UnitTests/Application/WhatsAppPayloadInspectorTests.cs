using System.Text;
using Orbita.Application.Channels;

namespace Orbita.UnitTests.Application;

public sealed class WhatsAppPayloadInspectorTests
{
    [Fact]
    public void ReadPhoneNumberId_FromARealisticPayload_FindsTheNestedId()
    {
        var payload = Encoding.UTF8.GetBytes(
            """
            {
              "object": "whatsapp_business_account",
              "entry": [{
                "id": "waba-1",
                "changes": [{
                  "value": {
                    "messaging_product": "whatsapp",
                    "metadata": { "display_phone_number": "573001112233", "phone_number_id": "1234567890" },
                    "messages": [{ "from": "573000000000", "id": "wamid.abc", "type": "text", "text": { "body": "hola" } }]
                  },
                  "field": "messages"
                }]
              }]
            }
            """);

        Assert.Equal("1234567890", WhatsAppPayloadInspector.ReadPhoneNumberId(payload));
    }

    [Fact]
    public void ReadPhoneNumberId_WithoutTheProperty_ReturnsNull()
        => Assert.Null(WhatsAppPayloadInspector.ReadPhoneNumberId(Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account"}""")));

    [Fact]
    public void ReadPhoneNumberId_WithMalformedJson_ReturnsNullInsteadOfThrowing()
        => Assert.Null(WhatsAppPayloadInspector.ReadPhoneNumberId(Encoding.UTF8.GetBytes("not json")));
}
