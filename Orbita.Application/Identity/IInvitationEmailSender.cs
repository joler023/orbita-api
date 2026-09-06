namespace Orbita.Application.Identity;

/// <summary>
/// Port for delivering the invitation email (ORB-A07). No real provider is wired up
/// yet — Notifications (Resend/SES per the DAS) is a separate module/historia — so the
/// Infrastructure implementation only logs the accept link for local development.
/// Takes the raw token rather than a built URL: turning it into a link is a
/// presentation/configuration concern (which frontend base URL) that belongs with
/// whatever Infrastructure already reads Jwt:*/Cors:* from, not with Application.
/// </summary>
public interface IInvitationEmailSender
{
    Task SendAsync(string email, string tenantName, string rawInvitationToken, CancellationToken cancellationToken);
}
