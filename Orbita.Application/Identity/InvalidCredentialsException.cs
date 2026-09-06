namespace Orbita.Application.Identity;

/// <summary>
/// Thrown for an unknown email, a wrong password, or a locked-out account — always the
/// same exception and the same message, so the Api layer cannot accidentally leak
/// which case it was.
/// </summary>
public sealed class InvalidCredentialsException() : Exception("Invalid email or password.");
