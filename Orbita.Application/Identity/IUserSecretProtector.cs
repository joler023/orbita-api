namespace Orbita.Application.Identity;

/// <summary>
/// Encrypts small secrets (currently just the TOTP secret, ORB-A11) before they touch
/// the database. Stands in for a real KMS-backed encryption (orbita-schema.dbml calls
/// for channel credentials to be KMS-encrypted; this is the same idea applied to a
/// secret that predates any KMS integration existing in this codebase) — swap the
/// Infrastructure implementation for one backed by AWS KMS/Secrets Manager once that
/// exists, without touching anything that calls this port.
/// </summary>
public interface IUserSecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}
