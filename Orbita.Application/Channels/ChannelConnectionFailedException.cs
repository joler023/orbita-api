namespace Orbita.Application.Channels;

/// <summary>Meta rejected a step of the connection flow (code exchange, account lookup, webhook subscription).</summary>
public sealed class ChannelConnectionFailedException(string message) : Exception(message);
