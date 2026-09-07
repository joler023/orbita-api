using System.Security.Cryptography;
using OtpNet;
using Orbita.Application.Identity;

namespace Orbita.Infrastructure.Identity;

/// <summary>RFC 6238 TOTP via Otp.NET (ORB-A11).</summary>
public sealed class OtpTotpProvider : ITotpProvider
{
    // 160 bits — RFC 4226's recommended HOTP/TOTP secret size, and what every mainstream
    // authenticator app (Google Authenticator, Authy, 1Password, etc.) expects.
    private const int SecretSizeBytes = 20;

    public string GenerateSecret() => Base32Encoding.ToString(RandomNumberGenerator.GetBytes(SecretSizeBytes));

    public string BuildProvisioningUri(string secret, string accountName, string issuer)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        var encodedIssuer = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    public bool ValidateCode(string secret, string code)
    {
        var totp = new Totp(Base32Encoding.ToBytes(secret));

        // A one-step window on each side tolerates the clock drift/typing delay every
        // real authenticator app setup runs into — RFC 6238 explicitly recommends this.
        return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }
}
