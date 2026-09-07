namespace Orbita.Application.Billing;

/// <summary>
/// <paramref name="DownloadUrl"/> is the provider's own hosted invoice/receipt page —
/// this API never generates or stores invoice PDFs itself, both Stripe and Wompi
/// already host one per transaction.
/// </summary>
public sealed record InvoiceSummary(string Id, DateTimeOffset IssuedAt, decimal AmountDue, string Currency, string Status, string? DownloadUrl);
