using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;

namespace Orbita.Application.Identity;

public sealed class CurrentUserService(
    IUserRepository userRepository,
    IMembershipRepository membershipRepository,
    ITenantRepository tenantRepository,
    IUnitOfWork unitOfWork) : ICurrentUserService
{
    public async Task<CurrentUser> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"The signed-in user {userId} no longer exists.");

        // The only read in the product that crosses tenants, and the only place
        // `app.user_id` is ever set. Everything it returns is keyed on the id above,
        // which came from the token.
        var memberships = await unitOfWork.QueryInUserScopeAsync(
            userId,
            ct => membershipRepository.ListAcceptedByUserAsync(userId, ct),
            cancellationToken);

        if (memberships.Count == 0)
        {
            return new CurrentUser(user.Id, user.Email, user.FullName, []);
        }

        // `tenants` has no Row Level Security of its own — it carries no tenant_id to
        // filter on — so these ids are the whole access check. They are the tenants of
        // the caller's own memberships and nothing else.
        var tenants = (await tenantRepository.ListByIdsAsync(
                [.. memberships.Select(membership => membership.TenantId)],
                cancellationToken))
            .Where(tenant => tenant.IsActive)
            .ToDictionary(tenant => tenant.Id);

        return new CurrentUser(
            user.Id,
            user.Email,
            user.FullName,
            [.. memberships
                // A membership whose organization was deactivated is dropped rather than
                // returned as a broken entry: offering one that cannot be opened is worse
                // than not offering it.
                .Where(membership => tenants.ContainsKey(membership.TenantId))
                .Select(membership => new CurrentUserMembership(
                    membership.TenantId,
                    tenants[membership.TenantId].Slug,
                    tenants[membership.TenantId].Name,
                    membership.Role))
                .OrderBy(membership => membership.Name, StringComparer.OrdinalIgnoreCase)]);
    }
}
