using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Orbita.Application.Billing;
using Orbita.Domain.Billing;
using Orbita.Infrastructure.Billing;

namespace Orbita.IntegrationTests;

/// <summary>
/// Tests WompiPaymentProvider's webhook checksum verification directly — self-contained
/// HMAC-style hashing against a configured secret, computed here the same way Wompi's
/// docs specify, no live Wompi account needed. The rest of WompiPaymentProvider
/// (creating payment sources/transactions) needs a real or sandbox account — see this
/// class's own summary and CLAUDE.md's Billing section.
/// </summary>
public sealed class WompiPaymentProviderTests
{
    private const string EventsSecret = "test_events_secret";

    private readonly WompiPaymentProvider _sut = new(new HttpClient(), BuildConfiguration());

    [Fact]
    public void ParseWebhookEvent_WithAValidChecksum_ReturnsTheSubscriptionState()
    {
        const string transactionId = "1234-1609459200-12345";
        const string status = "APPROVED";
        const long amountInCents = 4490000;
        const long paymentSourceId = 987654;
        const long timestamp = 1780000000;

        var checksum = ComputeChecksum(transactionId, status, amountInCents, timestamp);
        var payload = $$"""
            {
              "event": "transaction.updated",
              "data": {
                "transaction": {
                  "id": "{{transactionId}}",
                  "status": "{{status}}",
                  "amount_in_cents": {{amountInCents}},
                  "payment_source_id": {{paymentSourceId}}
                }
              },
              "timestamp": {{timestamp}},
              "signature": {
                "properties": ["transaction.id", "transaction.status", "transaction.amount_in_cents"],
                "checksum": "{{checksum}}"
              }
            }
            """;

        var state = _sut.ParseWebhookEvent(payload, signatureHeader: string.Empty);

        Assert.NotNull(state);
        Assert.Equal(paymentSourceId.ToString(), state!.ProviderSubscriptionId);
        Assert.Equal(SubscriptionStatus.Active, state.Status);
    }

    [Fact]
    public void ParseWebhookEvent_WithATamperedChecksum_Throws()
    {
        const string payload = """
            {
              "event": "transaction.updated",
              "data": { "transaction": { "id": "1", "status": "APPROVED", "amount_in_cents": 100, "payment_source_id": 1 } },
              "timestamp": 1780000000,
              "signature": { "properties": ["transaction.id"], "checksum": "not-the-real-checksum" }
            }
            """;

        Assert.Throws<InvalidWebhookSignatureException>(() => _sut.ParseWebhookEvent(payload, signatureHeader: string.Empty));
    }

    [Fact]
    public void ParseWebhookEvent_ForAnUnrelatedEventType_ReturnsNull()
    {
        const string payload = """{"event":"nequi_token.updated","data":{}}""";

        var state = _sut.ParseWebhookEvent(payload, signatureHeader: string.Empty);

        Assert.Null(state);
    }

    private static string ComputeChecksum(string transactionId, string status, long amountInCents, long timestamp)
    {
        var concatenated = $"{transactionId}{status}{amountInCents}{timestamp}{EventsSecret}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(concatenated)));
    }

    private static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Wompi:BaseUrl"] = "https://sandbox.wompi.co/v1/",
                ["Billing:Wompi:PublicKey"] = "pub_test_unused",
                ["Billing:Wompi:PrivateKey"] = "prv_test_unused",
                ["Billing:Wompi:EventsSecret"] = EventsSecret,
            })
            .Build();
}
