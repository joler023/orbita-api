using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Orbita.Application.Billing;
using DomainPlan = Orbita.Domain.Billing.Plan;
using DomainPaymentProvider = Orbita.Domain.Billing.PaymentProvider;
using DomainSubscriptionStatus = Orbita.Domain.Billing.SubscriptionStatus;

namespace Orbita.Infrastructure.Billing;

/// <summary>
/// The Colombian rail (ORB-A12), talking to Wompi's REST API directly (Wompi has no
/// official .NET SDK). Needs `Billing:Wompi:PublicKey`, `Billing:Wompi:PrivateKey`,
/// and `Billing:Wompi:EventsSecret` configured before any call here will succeed —
/// all empty placeholders in appsettings until a real account is connected.
///
/// <para>
/// <b>Wompi has no subscription object, unlike Stripe</b> — only one-off transactions
/// against a reusable, tokenized <c>payment_source</c>. This class maps that onto
/// <see cref="IPaymentProvider"/> as follows, which is worth understanding before
/// relying on it:
/// </para>
/// <list type="bullet">
/// <item><see cref="CreateCustomerAsync"/> creates the payment_source and returns its
/// id — there is no separate "customer" resource, so this id doubles as
/// <c>Subscription.ProviderCustomerId</c>.</item>
/// <item><see cref="CreateSubscriptionAsync"/> makes the first charge against that
/// source and reuses its id as <c>Subscription.ProviderSubscriptionId</c> too (both
/// columns end up holding the same value for a Wompi tenant, deliberately).</item>
/// <item><see cref="ChangeSubscriptionAsync"/> charges the new plan's full price
/// immediately — Wompi has no billing cycle to prorate against, so "cambio de plan
/// con prorrateo" does not apply here the way it does for Stripe.</item>
/// <item><see cref="CancelSubscriptionAsync"/> is a no-op: there is nothing to cancel
/// on Wompi's side. Cancellation only has meaning once a recurring-billing scheduler
/// exists to re-charge active Wompi subscriptions each period — <b>that scheduler
/// does not exist yet</b> and is required before Wompi billing can go live. Until
/// then, cancelling just stops that (nonexistent) scheduler from targeting this
/// tenant.</item>
/// <item><see cref="ListInvoicesAsync"/> returns an empty list — Wompi's API has no
/// "list transactions for a customer" endpoint, only <c>GET /transactions/{id}</c> for
/// an already-known id. A real implementation needs to persist each transaction id
/// locally as it's created and look them up individually.</item>
/// </list>
/// </summary>
public sealed class WompiPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _publicKey;
    private readonly string _eventsSecret;

    public WompiPaymentProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri(configuration["Billing:Wompi:BaseUrl"] ?? "https://sandbox.wompi.co/v1/");
        _publicKey = configuration["Billing:Wompi:PublicKey"] ?? string.Empty;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", configuration["Billing:Wompi:PrivateKey"] ?? string.Empty);
        _eventsSecret = configuration["Billing:Wompi:EventsSecret"] ?? string.Empty;
    }

    public DomainPaymentProvider Kind => DomainPaymentProvider.Wompi;

    public async Task<string> CreateCustomerAsync(string email, string name, string paymentMethodToken, CancellationToken cancellationToken)
    {
        var acceptanceToken = await FetchAcceptanceTokenAsync(cancellationToken);

        var response = await _httpClient.PostAsJsonAsync(
            "payment_sources",
            new
            {
                type = "CARD",
                token = paymentMethodToken,
                customer_email = email,
                acceptance_token = acceptanceToken,
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<WompiEnvelope<WompiPaymentSource>>(cancellationToken: cancellationToken);
        return payload!.Data.Id.ToString(CultureInfo.InvariantCulture);
    }

    public async Task<ProviderSubscriptionState> CreateSubscriptionAsync(string providerCustomerId, DomainPlan plan, CancellationToken cancellationToken)
    {
        var transaction = await CreateTransactionAsync(providerCustomerId, plan.PriceAmount, plan.PriceCurrency, cancellationToken);
        return new ProviderSubscriptionState(providerCustomerId, MapStatus(transaction.Status), DateTimeOffset.UtcNow.AddMonths(1));
    }

    public async Task<ProviderSubscriptionState> ChangeSubscriptionAsync(string providerSubscriptionId, DomainPlan newPlan, CancellationToken cancellationToken)
    {
        var transaction = await CreateTransactionAsync(providerSubscriptionId, newPlan.PriceAmount, newPlan.PriceCurrency, cancellationToken);
        return new ProviderSubscriptionState(providerSubscriptionId, MapStatus(transaction.Status), DateTimeOffset.UtcNow.AddMonths(1));
    }

    public Task CancelSubscriptionAsync(string providerSubscriptionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string providerCustomerId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<InvoiceSummary>>([]);

    public ProviderSubscriptionState? ParseWebhookEvent(string payload, string signatureHeader)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.GetProperty("event").GetString() != "transaction.updated")
        {
            return null;
        }

        var data = root.GetProperty("data");
        var signature = root.GetProperty("signature");
        var timestamp = root.GetProperty("timestamp").GetRawText();
        var expectedChecksum = signature.GetProperty("checksum").GetString();

        // Per Wompi's webhook signature docs: concatenate the raw values of each
        // property named in signature.properties (in order), then the event
        // timestamp, then the events secret, and SHA-256 the result.
        var concatenated = new StringBuilder();
        foreach (var property in signature.GetProperty("properties").EnumerateArray())
        {
            concatenated.Append(ResolveJsonPath(data, property.GetString()!));
        }

        concatenated.Append(timestamp).Append(_eventsSecret);

        var computedChecksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(concatenated.ToString())));
        if (!string.Equals(computedChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidWebhookSignatureException();
        }

        var transaction = data.GetProperty("transaction");
        if (!transaction.TryGetProperty("payment_source_id", out var paymentSourceIdProperty))
        {
            return null;
        }

        var status = transaction.GetProperty("status").GetString()!;
        return new ProviderSubscriptionState(paymentSourceIdProperty.GetRawText(), MapStatus(status), null);
    }

    private async Task<string> FetchAcceptanceTokenAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"merchants/{_publicKey}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<WompiEnvelope<WompiMerchant>>(cancellationToken: cancellationToken);
        return payload!.Data.PresignedAcceptance.AcceptanceToken;
    }

    private async Task<WompiTransaction> CreateTransactionAsync(string paymentSourceId, decimal amount, string currency, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "transactions",
            new
            {
                amount_in_cents = (long)(amount * 100),
                currency,
                payment_source_id = long.Parse(paymentSourceId, CultureInfo.InvariantCulture),
                reference = Guid.NewGuid().ToString("N"),
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<WompiEnvelope<WompiTransaction>>(cancellationToken: cancellationToken);
        return envelope!.Data;
    }

    /// <param name="data">The webhook payload's "data" object — property paths like "transaction.id" are relative to it.</param>
    private static string ResolveJsonPath(JsonElement data, string dottedPath)
    {
        var current = data;
        foreach (var segment in dottedPath.Split('.'))
        {
            current = current.GetProperty(segment);
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString()! : current.GetRawText();
    }

    private static DomainSubscriptionStatus MapStatus(string wompiStatus) => wompiStatus switch
    {
        "APPROVED" => DomainSubscriptionStatus.Active,
        "DECLINED" or "ERROR" => DomainSubscriptionStatus.PastDue,
        "VOIDED" => DomainSubscriptionStatus.Canceled,
        _ => DomainSubscriptionStatus.Trialing, // PENDING
    };
}
