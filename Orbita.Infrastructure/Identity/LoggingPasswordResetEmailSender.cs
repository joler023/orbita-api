using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orbita.Application.Identity;

namespace Orbita.Infrastructure.Identity;

/// <summary>
/// Stands in for Notifications/Resend/SES, which isn't wired up yet — logs the reset
/// link instead of emailing it, same "dev-mode email backend" pattern as
/// LoggingInvitationEmailSender. Replace with a real sender when the Notifications
/// module lands; IPasswordResetEmailSender is the seam for that.
/// </summary>
public sealed class LoggingPasswordResetEmailSender(IConfiguration configuration, ILogger<LoggingPasswordResetEmailSender> logger)
    : IPasswordResetEmailSender
{
    public Task SendAsync(string email, string rawResetToken, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
        var resetUrl = $"{baseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawResetToken)}";

        logger.LogInformation("Password reset for {Email}: {ResetUrl}", email, resetUrl);

        return Task.CompletedTask;
    }
}
