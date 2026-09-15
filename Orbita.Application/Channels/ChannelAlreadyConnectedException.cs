namespace Orbita.Application.Channels;

/// <summary>The Meta account (phone number / IG account) is already connected to a different tenant.</summary>
public sealed class ChannelAlreadyConnectedException()
    : Exception("This account is already connected to another organization.");
