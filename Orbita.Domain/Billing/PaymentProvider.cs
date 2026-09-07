namespace Orbita.Domain.Billing;

/// <summary>The two payment rails ORB-A12 supports: Stripe (international) and Wompi (Colombia).</summary>
public enum PaymentProvider
{
    Stripe,
    Wompi,
}
