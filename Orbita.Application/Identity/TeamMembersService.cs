using Orbita.Application.Audit;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Identity;

public sealed class TeamMembersService(
    IMembershipRepository membershipRepository,
    IUserRepository userRepository,
    ITenantAuthorizationService authorizationService,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork) : ITeamMembersService
{
    public async Task<IReadOnlyList<TeamMemberSummary>> ListAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewTeam, cancellationToken);

        var memberships = await unitOfWork.QueryInTenantScopeAsync(
            ct => membershipRepository.GetActiveByTenantAsync(tenantId, ct),
            cancellationToken);

        var users = await userRepository.GetByIdsAsync(memberships.Select(m => m.UserId).ToArray(), cancellationToken);
        var usersById = users.ToDictionary(u => u.Id);

        return memberships
            .Select(m => ToSummary(m, usersById[m.UserId]))
            .ToList();
    }

    public async Task<TeamMemberSummary> ChangeRoleAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid membershipId,
        MemberRole newRole,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageTeam, cancellationToken);
        var membership = await RequireActiveMembershipAsync(tenantId, membershipId, cancellationToken);

        if (membership.Role == MemberRole.Owner && newRole != MemberRole.Owner)
        {
            await EnsureNotTheLastOwnerAsync(tenantId, cancellationToken);
        }

        var previousRole = membership.Role;
        membership.ChangeRole(newRole);
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "membership.role_changed",
            nameof(Membership),
            membership.Id,
            new { from = previousRole.ToString(), to = newRole.ToString() },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var user = await RequireUserAsync(membership.UserId, cancellationToken);
        return ToSummary(membership, user);
    }

    public async Task RemoveAsync(Guid tenantId, Guid callerUserId, Guid membershipId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageTeam, cancellationToken);
        var membership = await RequireActiveMembershipAsync(tenantId, membershipId, cancellationToken);

        if (membership.Role == MemberRole.Owner)
        {
            await EnsureNotTheLastOwnerAsync(tenantId, cancellationToken);
        }

        membership.Deactivate();
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "membership.removed",
            nameof(Membership),
            membership.Id,
            new { role = membership.Role.ToString() },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureNotTheLastOwnerAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var ownerCount = await unitOfWork.QueryInTenantScopeAsync(
            ct => membershipRepository.CountActiveByTenantAndRoleAsync(tenantId, MemberRole.Owner, ct),
            cancellationToken);

        if (ownerCount <= 1)
        {
            throw new CannotRemoveLastOwnerException();
        }
    }

    private async Task<Membership> RequireActiveMembershipAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
    {
        var membership = await unitOfWork.QueryInTenantScopeAsync(
            ct => membershipRepository.GetByIdAsync(membershipId, ct),
            cancellationToken);

        // TenantId is re-checked even though the query filter already scoped the
        // lookup to tenantId — cheap defense in depth against a future filter bug.
        if (membership is null || membership.TenantId != tenantId || !membership.IsActive)
        {
            throw new MemberNotFoundException();
        }

        return membership;
    }

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken cancellationToken)
        => await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

    private static TeamMemberSummary ToSummary(Membership membership, User user)
        => new(membership.Id, user.Id, user.Email, user.FullName, membership.Role, membership.IsPending, membership.CreatedAt);
}
