using System.Collections.Concurrent;
using Orbita.Application.Identity;

namespace Orbita.IntegrationTests.TestSupport;

/// <summary>
/// Replaces the real (logging-only) IPasswordResetEmailSender in tests, so a test can
/// grab the raw reset token that would otherwise only ever exist inside an email
/// nobody sends yet — see ORB-A10.
/// </summary>
public sealed class CapturingPasswordResetEmailSender : IPasswordResetEmailSender
{
    // ConcurrentQueue (FIFO), not ConcurrentBag, so LatestTokenFor reliably returns the
    // newest of several tokens issued for the same email — see CapturingInvitationEmailSender.
    private readonly ConcurrentQueue<(string Email, string RawToken)> _sent = [];

    public Task SendAsync(string email, string rawResetToken, CancellationToken cancellationToken)
    {
        _sent.Enqueue((email, rawResetToken));
        return Task.CompletedTask;
    }

    public string LatestTokenFor(string email) => _sent.Last(s => s.Email == email).RawToken;
}
