using Orbita.Domain.Identity;

namespace Orbita.Application.Identity;

/// <summary>
/// Lists, changes the role of, and removes team members (ORB-A08). Complements
/// <see cref="ITeamInvitationService"/>, which only covers inviting/accepting — this is
/// for administering memberships that already exist.
/// </summary>
public interface ITeamMembersService
{
    /// <summary>All active memberships in the tenant, pending invitations included.</summary>
    /// <exception cref="ForbiddenException">Caller lacks the ViewTeam permission.</exception>
    Task<IReadOnlyList<TeamMemberSummary>> ListAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">Caller lacks the ManageTeam permission.</exception>
    /// <exception cref="MemberNotFoundException">No active membership with that id in this tenant.</exception>
    /// <exception cref="CannotRemoveLastOwnerException">The target is the tenant's only Owner.</exception>
    Task<TeamMemberSummary> ChangeRoleAsync(Guid tenantId, Guid callerUserId, Guid membershipId, MemberRole newRole, CancellationToken cancellationToken);

    /// <exception cref="ForbiddenException">Caller lacks the ManageTeam permission.</exception>
    /// <exception cref="MemberNotFoundException">No active membership with that id in this tenant.</exception>
    /// <exception cref="CannotRemoveLastOwnerException">The target is the tenant's only Owner.</exception>
    Task RemoveAsync(Guid tenantId, Guid callerUserId, Guid membershipId, CancellationToken cancellationToken);
}
