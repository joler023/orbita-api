using System.Collections.Concurrent;
using Orbita.Application.Identity;

namespace Orbita.IntegrationTests.TestSupport;

/// <summary>
/// Replaces the real (logging-only) IInvitationEmailSender in tests, so a test can
/// grab the raw invitation token that would otherwise only ever exist inside an email
/// nobody sends yet — see ORB-A07.
/// </summary>
public sealed class CapturingInvitationEmailSender : IInvitationEmailSender
{
    // ConcurrentBag does not preserve insertion order on enumeration — resending
    // needs LatestTokenFor to reliably return the newest of two tokens for the same
    // email, so this uses ConcurrentQueue (FIFO) instead.
    private readonly ConcurrentQueue<(string Email, string TenantName, string RawToken)> _sent = [];

    public Task SendAsync(string email, string tenantName, string rawInvitationToken, CancellationToken cancellationToken)
    {
        _sent.Enqueue((email, tenantName, rawInvitationToken));
        return Task.CompletedTask;
    }

    public string LatestTokenFor(string email) => _sent.Last(s => s.Email == email).RawToken;
}
