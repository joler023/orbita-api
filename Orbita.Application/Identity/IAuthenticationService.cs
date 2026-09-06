namespace Orbita.Application.Identity;

public interface IAuthenticationService
{
    /// <exception cref="InvalidCredentialsException">
    /// Unknown email, wrong password, or the account is locked out.
    /// </exception>
    Task<AuthenticationResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <exception cref="InvalidRefreshTokenException">
    /// The token is unknown, expired, or already used (which also revokes its whole family).
    /// </exception>
    Task<AuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Idempotent: revoking an already-invalid token is not an error.</summary>
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);
}
