namespace Orbita.Application.Identity;

/// <summary>Token is unknown, expired, or already used (ORB-A10).</summary>
public sealed class InvalidPasswordResetException() : Exception("Invalid or expired password reset token.");
