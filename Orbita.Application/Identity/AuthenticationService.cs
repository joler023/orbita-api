using System.Security.Cryptography;
using System.Text;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Identity;

/// <summary>
/// Login, refresh-token rotation and logout (ORB-A06). Every write goes through a
/// single <see cref="IUnitOfWork.SaveChangesAsync"/> call per public method, so a
/// failed-login counter bump or a token rotation is never left half-applied.
/// </summary>
public sealed class AuthenticationService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IAccessTokenIssuer accessTokenIssuer,
    IPasswordHasher passwordHasher,
    ITwoFactorService twoFactorService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAuthenticationService
{
    public const int MaxFailedLoginAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public async Task<AuthenticationResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (user is null || user.IsLockedOut(now))
        {
            throw new InvalidCredentialsException();
        }

        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            user.RegisterFailedLogin(now, MaxFailedLoginAttempts, LockoutDuration);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new InvalidCredentialsException();
        }

        if (user.HasTwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
            {
                // The password was right — this is not a fact worth hiding the way a
                // wrong password or unknown email is, so it gets its own exception
                // instead of folding into InvalidCredentialsException (ORB-A11).
                throw new TwoFactorRequiredException();
            }

            if (!await twoFactorService.VerifyLoginCodeAsync(user.Id, request.TwoFactorCode, cancellationToken))
            {
                // A wrong code at this point *is* folded back into the generic
                // failure, and counts against the same lockout counter as a wrong
                // password — brute-forcing the second factor should be exactly as
                // expensive as brute-forcing the first.
                user.RegisterFailedLogin(now, MaxFailedLoginAttempts, LockoutDuration);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                throw new InvalidCredentialsException();
            }
        }

        user.ResetFailedLogins();
        user.RecordLogin(now);

        var (accessToken, rawRefreshToken, refreshTokenExpiresAt) =
            await IssueTokenPairAsync(user.Id, familyId: null, now, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResult(user, accessToken, rawRefreshToken, refreshTokenExpiresAt);
    }

    public async Task<AuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var presented = await refreshTokenRepository.GetByTokenHashAsync(Hash(refreshToken), cancellationToken);

        if (presented is null)
        {
            throw new InvalidRefreshTokenException();
        }

        if (presented.RevokedAt is not null)
        {
            // A token that was already rotated away is being presented again — the
            // strongest available signal that it (and therefore its whole family)
            // has been stolen. Revoke everything issued from the same login.
            // RevokeFamilyAsync is a bulk update that commits immediately, bypassing
            // the change tracker (and IUnitOfWork) entirely — there is nothing else
            // to save on this path.
            await refreshTokenRepository.RevokeFamilyAsync(presented.FamilyId, now, cancellationToken);
            throw new InvalidRefreshTokenException();
        }

        if (presented.ExpiresAt <= now)
        {
            throw new InvalidRefreshTokenException();
        }

        var user = await userRepository.GetByIdAsync(presented.UserId, cancellationToken)
            ?? throw new InvalidRefreshTokenException();

        var (accessToken, rawRefreshToken, refreshTokenExpiresAt) =
            await IssueTokenPairAsync(user.Id, presented.FamilyId, now, cancellationToken, presented);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResult(user, accessToken, rawRefreshToken, refreshTokenExpiresAt);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var presented = await refreshTokenRepository.GetByTokenHashAsync(Hash(refreshToken), cancellationToken);
        if (presented is null || !presented.IsActive(now))
        {
            return;
        }

        presented.Revoke(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<(IssuedAccessToken AccessToken, string RawRefreshToken, DateTimeOffset RefreshTokenExpiresAt)> IssueTokenPairAsync(
        Guid userId,
        Guid? familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        RefreshToken? replacing = null)
    {
        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var newToken = familyId is { } existingFamilyId
            ? RefreshToken.IssueInFamily(userId, Hash(rawRefreshToken), existingFamilyId, now, RefreshTokenLifetime)
            : RefreshToken.IssueNewFamily(userId, Hash(rawRefreshToken), now, RefreshTokenLifetime);

        replacing?.RevokeReplacedBy(newToken.Id, now);

        await refreshTokenRepository.AddAsync(newToken, cancellationToken);

        var accessToken = accessTokenIssuer.Issue(userId, now);

        return (accessToken, rawRefreshToken, newToken.ExpiresAt);
    }

    private static AuthenticationResult ToResult(User user, IssuedAccessToken accessToken, string rawRefreshToken, DateTimeOffset refreshTokenExpiresAt)
        => new(
            accessToken.Value,
            accessToken.ExpiresAt,
            rawRefreshToken,
            refreshTokenExpiresAt,
            user.Id,
            user.Email,
            user.FullName);

    private static string Hash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
