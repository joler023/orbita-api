namespace Orbita.Application.Identity;

/// <summary>
/// ORB-A16: who is signed in, and which organizations they can act as.
///
/// <para><b>Why this is its own service.</b> <see cref="IAuthenticationService"/> decides
/// whether someone may have a session; this describes a session that already exists. They
/// change for different reasons — one for credential and token rules, the other for what
/// the shell of the application needs to render.</para>
///
/// <para><b>Why it exists at all.</b> The access token deliberately carries no tenant
/// claim, and the tenant is a route parameter everywhere else. So immediately after
/// signing in there is nothing that tells a client which organization to ask for, and on
/// a device that has never been used it cannot fall back to anything it remembered. This
/// is the endpoint that answers that.</para>
/// </summary>
public interface ICurrentUserService
{
    /// <param name="userId">
    /// From the authenticated session's <c>sub</c> claim — never from the request. It is
    /// the entire access check for what comes back.
    /// </param>
    Task<CurrentUser> GetAsync(Guid userId, CancellationToken cancellationToken);
}

/// <param name="Memberships">
/// The organizations this person belongs to. Empty is a normal answer, not an error: it
/// means they were invited and removed, or signed up without creating one yet, and the
/// client should offer to create one rather than show a broken screen.
/// </param>
public sealed record CurrentUser(
    Guid UserId,
    string Email,
    string FullName,
    IReadOnlyList<CurrentUserMembership> Memberships);

/// <param name="Role">
/// What this person may do in that organization. Sent so a client can hide actions it
/// knows will be refused — but the 403 from the backend is the real control, never this
/// (ORB-A08 is explicit that hiding a button is not access control).
/// </param>
public sealed record CurrentUserMembership(
    Guid TenantId,
    string Slug,
    string Name,
    Orbita.Domain.Identity.MemberRole Role);
