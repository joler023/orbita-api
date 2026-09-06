namespace Orbita.Application.Identity;

/// <summary>Thrown when a refresh token is unknown, expired, or already used.</summary>
public sealed class InvalidRefreshTokenException() : Exception("Invalid or expired refresh token.");
