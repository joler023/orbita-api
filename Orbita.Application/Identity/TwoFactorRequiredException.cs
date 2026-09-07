namespace Orbita.Application.Identity;

/// <summary>
/// The password was correct but the account has two-factor authentication enabled and
/// no code was submitted (ORB-A11). Distinct from <see cref="InvalidCredentialsException"/>
/// on purpose — unlike a wrong password or unknown email, this is not a fact worth
/// hiding: the caller already proved they know the password.
/// </summary>
public sealed class TwoFactorRequiredException() : Exception("Two-factor authentication code required.");
