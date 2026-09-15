namespace Orbita.Domain.Inbox;

/// <summary>Determines what a message actually costs on Meta's side. <see cref="Service"/> (inside the 24h window) is free.</summary>
public enum MessageCategory
{
    Service,
    Marketing,
    Utility,
    Authentication,
}
