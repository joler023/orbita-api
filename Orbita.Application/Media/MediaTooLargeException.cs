namespace Orbita.Application.Media;

public sealed class MediaTooLargeException() : Exception("Media file exceeds the maximum allowed size for its type.");
