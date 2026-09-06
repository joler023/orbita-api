namespace Orbita.Application.Identity;

public interface ITeamInvitationService
{
    /// <exception cref="ForbiddenException">The caller is not an Owner/Admin of the tenant.</exception>
    /// <exception cref="MembershipAlreadyExistsException">The email is already a member or already invited.</exception>
    Task<TeamInvitationSummary> InviteAsync(
        Guid tenantId,
        Guid callerUserId,
        InviteTeamMemberRequest request,
        CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">The caller is not an Owner/Admin of the tenant.</exception>
    /// <exception cref="InvitationNotFoundException">No pending invitation with that id exists in the tenant.</exception>
    Task<TeamInvitationSummary> ResendAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid membershipId,
        CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">The caller is not an Owner/Admin of the tenant.</exception>
    /// <exception cref="InvitationNotFoundException">No pending invitation with that id exists in the tenant.</exception>
    Task RevokeAsync(Guid tenantId, Guid callerUserId, Guid membershipId, CancellationToken cancellationToken);

    /// <exception cref="InvalidInvitationException">The token is unknown, expired, or already used.</exception>
    /// <exception cref="InvalidCredentialsException">
    /// The invited person already has an account and the supplied password does not match it.
    /// </exception>
    Task<AcceptInvitationResult> AcceptAsync(AcceptInvitationRequest request, CancellationToken cancellationToken);
}
