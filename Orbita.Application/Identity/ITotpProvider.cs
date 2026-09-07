namespace Orbita.Application.Identity;

/// <summary>
/// RFC 6238 TOTP generation/validation (ORB-A11), kept behind a port for the same
/// reason as <see cref="IPasswordHasher"/>: the algorithm is an infrastructure
/// concern, application code only needs to ask "is this code valid for this secret."
/// </summary>
public interface ITotpProvider
{
    /// <summary>A fresh random base32-encoded secret, one per setup attempt.</summary>
    string GenerateSecret();

    /// <summary>The otpauth:// URI an authenticator app's QR scanner understands.</summary>
    string BuildProvisioningUri(string secret, string accountName, string issuer);

    /// <summary>Allows a small clock-skew window, per how every authenticator app actually behaves.</summary>
    bool ValidateCode(string secret, string code);
}
