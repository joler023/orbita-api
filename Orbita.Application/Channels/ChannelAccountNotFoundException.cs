namespace Orbita.Application.Channels;

/// <summary>No channel account with that id in this tenant.</summary>
public sealed class ChannelAccountNotFoundException() : Exception("Channel account not found.");
