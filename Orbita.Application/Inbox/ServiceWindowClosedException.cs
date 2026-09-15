namespace Orbita.Application.Inbox;

/// <summary>More than 24h since the customer's last message — only an approved template can restart the conversation (ORB-B07).</summary>
public sealed class ServiceWindowClosedException() : Exception("The 24-hour service window is closed; send an approved template instead.");
