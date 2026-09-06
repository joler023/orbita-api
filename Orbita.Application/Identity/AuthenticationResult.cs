namespace Orbita.Application.Identity;

/// <summary>
/// Everything the Api layer needs to answer a login/refresh call: the JWT to put in
/// the access-token cookie, the raw refresh token to put in the refresh-token cookie
/// (only its hash is ever persisted — this is the one and only time the raw value
/// exists outside the client), and enough user data for the response body.
/// </summary>
public sealed record AuthenticationResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid UserId,
    string Email,
    string FullName);
