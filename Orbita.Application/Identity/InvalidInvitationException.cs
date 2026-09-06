namespace Orbita.Application.Identity;

/// <summary>The invitation token is unknown, expired, or already used.</summary>
public sealed class InvalidInvitationException() : Exception("Invalid or expired invitation.");
