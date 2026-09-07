namespace Orbita.Application.Identity;

/// <summary>
/// "Forgot your password" (ORB-A10): request a reset link, then redeem it.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Never reveals whether <paramref name="email"/> belongs to an account — the
    /// caller gets the same outcome either way, so this endpoint can't be used to
    /// enumerate registered accounts.
    /// </summary>
    Task RequestAsync(string email, CancellationToken cancellationToken);

    /// <exception cref="InvalidPasswordResetException">
    /// The token is unknown, expired, or already used.
    /// </exception>
    Task ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}
