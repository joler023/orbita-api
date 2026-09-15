using System.Text;
using Orbita.Application.Channels;

namespace Orbita.UnitTests.Application;

public sealed class InstagramPayloadInspectorTests
{
    [Fact]
    public void ReadAccountId_FromARealisticPayload_ReturnsTheFirstEntryId()
    {
        var payload = Encoding.UTF8.GetBytes(
            """
            {
              "object": "instagram",
              "entry": [{
                "id": "17841400000000000",
                "time": 1690000000,
                "messaging": [{ "sender": { "id": "123" }, "recipient": { "id": "17841400000000000" }, "message": { "text": "hola" } }]
              }]
            }
            """);

        Assert.Equal("17841400000000000", InstagramPayloadInspector.ReadAccountId(payload));
    }

    [Fact]
    public void ReadAccountId_WithoutAnEntryArray_ReturnsNull()
        => Assert.Null(InstagramPayloadInspector.ReadAccountId(Encoding.UTF8.GetBytes("""{"object":"instagram"}""")));

    [Fact]
    public void ReadAccountId_WithMalformedJson_ReturnsNullInsteadOfThrowing()
        => Assert.Null(InstagramPayloadInspector.ReadAccountId(Encoding.UTF8.GetBytes("not json")));
}
