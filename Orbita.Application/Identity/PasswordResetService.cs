using System.Security.Cryptography;
using System.Text;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Identity;

public sealed class PasswordResetService(
    IUserRepository userRepository,
    IPasswordResetTokenRepository passwordResetTokenRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IPasswordResetEmailSender emailSender,
    TimeProvider timeProvider) : IPasswordResetService
{
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    public async Task RequestAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null)
        {
            // Deliberately a no-op: the caller gets the same response either way
            // (ORB-A10's "no filtrar cuentas" requirement), so this path must never be
            // observably different — no extra delay, no different exception — from
            // the "user exists" path below.
            return;
        }

        var now = timeProvider.GetUtcNow();
        await passwordResetTokenRepository.InvalidateForUserAsync(user.Id, now, cancellationToken);

        var rawToken = GenerateRawToken();
        var resetToken = PasswordResetToken.Issue(user.Id, Hash(rawToken), now, ResetLifetime);
        await passwordResetTokenRepository.AddAsync(resetToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(user.Email, rawToken, cancellationToken);
    }

    public async Task ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var resetToken = await passwordResetTokenRepository.GetByTokenHashAsync(Hash(request.Token), cancellationToken);
        if (resetToken is null || !resetToken.IsValid(now))
        {
            throw new InvalidPasswordResetException();
        }

        var user = await userRepository.GetByIdAsync(resetToken.UserId, cancellationToken)
            ?? throw new InvalidPasswordResetException();

        user.SetPassword(passwordHasher.Hash(request.NewPassword), now);
        resetToken.MarkUsed(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // "Resetting invalidates all active sessions" (ORB-A10). A bulk update that
        // commits immediately, bypassing IUnitOfWork entirely — same pattern as
        // AuthenticationService.RevokeFamilyAsync — because refresh_tokens isn't
        // tenant-scoped and there is nothing else to save on this path.
        await refreshTokenRepository.RevokeAllForUserAsync(user.Id, now, cancellationToken);
    }

    private static string GenerateRawToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
