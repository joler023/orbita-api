using Orbita.Application.Identity;

namespace Orbita.Api.Identity;

/// <summary>
/// Shared between AuthController and TeamInvitationsController — accepting an
/// invitation logs the person in exactly the same way a normal login does.
/// </summary>
public static class AuthCookies
{
    public const string AccessToken = "access_token";
    public const string RefreshToken = "refresh_token";

    public static void Append(HttpResponse response, AuthenticationResult result, bool secure)
    {
        response.Cookies.Append(AccessToken, result.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Expires = result.AccessTokenExpiresAt,
        });

        // Scoped to /api/auth: the refresh token only ever needs to travel to the
        // refresh and logout endpoints, not on every request.
        response.Cookies.Append(RefreshToken, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = result.RefreshTokenExpiresAt,
        });
    }

    public static void Delete(HttpResponse response)
    {
        response.Cookies.Delete(AccessToken);
        response.Cookies.Delete(RefreshToken, new CookieOptions { Path = "/api/auth" });
    }
}
