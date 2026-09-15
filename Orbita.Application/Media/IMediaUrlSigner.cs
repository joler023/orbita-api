namespace Orbita.Application.Media;

/// <summary>Signs the presigned-URL-style tokens MediaController trusts as the whole auth check (ORB-B06) — a Data Protection stand-in for R2's own presigned URLs.</summary>
public interface IMediaUrlSigner
{
    string CreateToken(string operation, string key, DateTimeOffset expiresAt);

    /// <returns>The key, if the token is valid, unexpired, and issued for <paramref name="operation"/>; otherwise null.</returns>
    string? TryValidate(string token, string operation);
}
