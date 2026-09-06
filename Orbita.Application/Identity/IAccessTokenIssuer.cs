namespace Orbita.Application.Identity;

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Port for issuing the short-lived JWT access token (ADR-011: "la emisión de tokens
/// queda detrás de un puerto ITokenIssuer"). Deliberately carries only the user's
/// identity — no tenant claim yet, since resolving which organization a session acts
/// as is not part of ORB-A06's scope; the first authenticated, tenant-scoped endpoint
/// is what should decide how that gets embedded and enforced.
/// </summary>
public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(Guid userId, DateTimeOffset now);
}
