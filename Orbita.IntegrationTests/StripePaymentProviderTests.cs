using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Orbita.Application.Billing;
using Orbita.Domain.Billing;
using Orbita.Infrastructure.Billing;

namespace Orbita.IntegrationTests;

/// <summary>
/// Tests StripePaymentProvider's webhook signature verification directly — this needs
/// Infrastructure (only Orbita.IntegrationTests references it), but not a live Stripe
/// account: signature verification is self-contained HMAC against a configured secret,
/// which this test computes itself the same way Stripe does. See CLAUDE.md's Billing
/// section for why the rest of StripePaymentProvider (customer/subscription creation)
/// isn't covered here — those need a real or sandbox Stripe account.
/// </summary>
public sealed class StripePaymentProviderTests
{
    private const string WebhookSecret = "whsec_test_secret";

    private readonly StripePaymentProvider _sut = new(BuildConfiguration());

    [Fact]
    public void ParseWebhookEvent_WithAValidSignature_ReturnsTheSubscriptionState()
    {
        const string payload = """
            {
              "id": "evt_123",
              "object": "event",
              "type": "customer.subscription.updated",
              "data": {
                "object": {
                  "id": "sub_123",
                  "object": "subscription",
                  "status": "active",
                  "items": {
                    "object": "list",
                    "data": [
                      { "id": "si_123", "object": "subscription_item", "current_period_end": 1780000000, "current_period_start": 1777000000 }
                    ]
                  }
                }
              }
            }
            """;
        var signatureHeader = SignPayload(payload);

        var state = _sut.ParseWebhookEvent(payload, signatureHeader);

        Assert.NotNull(state);
        Assert.Equal("sub_123", state!.ProviderSubscriptionId);
        Assert.Equal(SubscriptionStatus.Active, state.Status);
        Assert.NotNull(state.CurrentPeriodEnd);
    }

    [Fact]
    public void ParseWebhookEvent_ForAnEventNotAboutASubscription_ReturnsNull()
    {
        const string payload = """
            {
              "id": "evt_456",
              "object": "event",
              "type": "invoice.paid",
              "data": {
                "object": {
                  "id": "in_123",
                  "object": "invoice"
                }
              }
            }
            """;
        var signatureHeader = SignPayload(payload);

        var state = _sut.ParseWebhookEvent(payload, signatureHeader);

        Assert.Null(state);
    }

    [Fact]
    public void ParseWebhookEvent_WithAnInvalidSignature_Throws()
    {
        const string payload = """{"id":"evt_789","object":"event","type":"customer.subscription.updated","data":{"object":{"id":"sub_1","object":"subscription","status":"active"}}}""";

        Assert.Throws<InvalidWebhookSignatureException>(() => _sut.ParseWebhookEvent(payload, "t=123,v1=not-a-real-signature"));
    }

    private static string SignPayload(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{payload}";
        var hash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();
        return $"t={timestamp},v1={hash}";
    }

    private static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Stripe:SecretKey"] = "sk_test_unused",
                ["Billing:Stripe:WebhookSecret"] = WebhookSecret,
            })
            .Build();
}
