using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Orbita.Application.Media;

namespace Orbita.Infrastructure.Media;

/// <summary>
/// Data Protection stand-in for a presigned R2 URL (ORB-B06). The token is
/// <c>Protect("operation|key|expiresAtUnixSeconds")</c> — Data Protection's own
/// <c>Protect(string)</c> already returns a URL-safe string, so no extra encoding is
/// needed. The signature is the entire authorization check for <c>MediaController</c>;
/// there is no separate session/auth check on those two routes.
/// </summary>
public sealed class MediaUrlSigner(IDataProtectionProvider provider, TimeProvider timeProvider) : IMediaUrlSigner
{
    private const string Purpose = "Orbita.MediaUrl.v1";
    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string CreateToken(string operation, string key, DateTimeOffset expiresAt)
        => _protector.Protect($"{operation}|{key}|{expiresAt.ToUnixTimeSeconds()}");

    public string? TryValidate(string token, string operation)
    {
        string payload;
        try
        {
            payload = _protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var parts = payload.Split('|', 3);
        if (parts.Length != 3 || parts[0] != operation || !long.TryParse(parts[2], out var expiresAtSeconds))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds) > timeProvider.GetUtcNow() ? parts[1] : null;
    }
}
