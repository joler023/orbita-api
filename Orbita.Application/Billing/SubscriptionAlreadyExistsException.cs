namespace Orbita.Application.Billing;

public sealed class SubscriptionAlreadyExistsException() : Exception("This tenant already has a subscription; use change-plan instead.");
