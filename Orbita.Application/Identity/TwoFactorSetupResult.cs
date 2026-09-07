namespace Orbita.Application.Identity;

/// <summary>
/// <paramref name="ProvisioningUri"/> is an otpauth:// URI (RFC covers the format
/// loosely; Google Authenticator's convention is the de facto standard every
/// authenticator app follows) — rendering it as a QR code is the frontend's job, this
/// API never generates images. <paramref name="Secret"/> (base32) is included too, for
/// the "can't scan a QR code" manual-entry fallback every authenticator app supports.
/// </summary>
public sealed record TwoFactorSetupResult(string Secret, string ProvisioningUri);
