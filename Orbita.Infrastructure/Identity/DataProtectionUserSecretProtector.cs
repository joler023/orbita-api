using Microsoft.AspNetCore.DataProtection;
using Orbita.Application.Identity;

namespace Orbita.Infrastructure.Identity;

/// <summary>
/// Stands in for KMS-backed encryption until Secrets Manager/KMS is wired up (ORB-A11)
/// — same "swap the Infrastructure implementation later" pattern as
/// LoggingInvitationEmailSender standing in for a real email provider. ASP.NET Core's
/// Data Protection keys are persisted locally by default, which is fine for a single
/// instance in development but not for a multi-instance production deployment;
/// replacing this with a real KMS-backed IUserSecretProtector is the actual fix, not
/// configuring a shared key ring for Data Protection.
/// </summary>
public sealed class DataProtectionUserSecretProtector : IUserSecretProtector
{
    private const string Purpose = "Orbita.TwoFactorSecret.v1";

    private readonly IDataProtector _protector;

    public DataProtectionUserSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
