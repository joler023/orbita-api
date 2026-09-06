using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orbita.Application.Identity;

namespace Orbita.Infrastructure.Identity;

/// <summary>
/// Stands in for Notifications/Resend/SES, which isn't wired up yet — logs the accept
/// link instead of emailing it, the same "dev-mode email backend" pattern most web
/// frameworks ship for local development. Replace with a real sender when the
/// Notifications module lands; IInvitationEmailSender is the seam for that.
/// </summary>
public sealed class LoggingInvitationEmailSender(IConfiguration configuration, ILogger<LoggingInvitationEmailSender> logger)
    : IInvitationEmailSender
{
    public Task SendAsync(string email, string tenantName, string rawInvitationToken, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
        var acceptUrl = $"{baseUrl.TrimEnd('/')}/accept-invite?token={Uri.EscapeDataString(rawInvitationToken)}";

        logger.LogInformation(
            "Invitation to join '{TenantName}' for {Email}: {AcceptUrl}",
            tenantName,
            email,
            acceptUrl);

        return Task.CompletedTask;
    }
}
