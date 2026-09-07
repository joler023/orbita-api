namespace Orbita.Application.Billing;

public sealed class SubscriptionNotFoundException() : Exception("No subscription found for this tenant.");
