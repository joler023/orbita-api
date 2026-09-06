using System.ComponentModel.DataAnnotations;

namespace Orbita.Application.Identity;

/// <summary>
/// <paramref name="Password"/> means two different things depending on the invited
/// person's state: for someone brand new (no account yet) it becomes their password;
/// for someone who already has an account, it must match their existing one — this
/// endpoint doubles as a login, since an email link alone should not be enough to act
/// as an arbitrary existing account.
/// </summary>
public sealed record AcceptInvitationRequest(
    [Required] string Token,
    [MaxLength(200)] string? FullName,
    [Required, MinLength(8), MaxLength(200)] string Password);
