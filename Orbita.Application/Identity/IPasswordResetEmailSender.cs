namespace Orbita.Application.Identity;

/// <summary>
/// Sends the "reset your password" email (ORB-A10). Takes the raw token, not a URL —
/// building the frontend link is Infrastructure's job, same as IInvitationEmailSender.
/// </summary>
public interface IPasswordResetEmailSender
{
    Task SendAsync(string email, string rawResetToken, CancellationToken cancellationToken);
}
