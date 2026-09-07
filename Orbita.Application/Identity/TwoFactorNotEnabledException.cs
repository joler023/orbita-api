namespace Orbita.Application.Identity;

/// <summary>Backup codes can't be regenerated for an account that never enabled two-factor (ORB-A11).</summary>
public sealed class TwoFactorNotEnabledException() : Exception("Two-factor authentication is not enabled for this user.");
