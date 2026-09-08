namespace Orbita.Application.Media;

public sealed class InvalidMediaSignatureException() : Exception("The media link is invalid, was issued for a different operation, or has expired.");
