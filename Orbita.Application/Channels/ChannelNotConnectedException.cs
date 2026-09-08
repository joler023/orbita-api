namespace Orbita.Application.Channels;

/// <summary>The conversation's channel account isn't Connected — nothing can be sent through it right now.</summary>
public sealed class ChannelNotConnectedException() : Exception("The channel account is not connected.");
