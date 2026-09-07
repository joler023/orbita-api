namespace Orbita.Application.Identity;

/// <summary>
/// The submitted TOTP/backup code doesn't match (ORB-A11). Used for the
/// already-authenticated setup/confirm/regenerate flows — at login time a wrong code
/// is reported as <see cref="InvalidCredentialsException"/> instead, same as a wrong
/// password, so the endpoint stays generic about why a login attempt failed.
/// </summary>
public sealed class InvalidTwoFactorCodeException() : Exception("Invalid two-factor authentication code.");
