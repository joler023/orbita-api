using System.Security.Cryptography;
using System.Text;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;

namespace Orbita.Application.Identity;

/// <summary>
/// Invites, resends, revokes, and accepts team invitations (ORB-A07). Caller
/// authorization ("must be able to manage this tenant's team") goes through
/// <see cref="ITenantAuthorizationService"/> (ORB-A08), not an inline role check.
/// </summary>
public sealed class TeamInvitationService(
    ITenantRepository tenantRepository,
    IUserRepository userRepository,
    IMembershipRepository membershipRepository,
    IInvitationTokenRepository invitationTokenRepository,
    ITenantContextSetter tenantContextSetter,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IInvitationEmailSender emailSender,
    ITenantAuthorizationService authorizationService,
    TimeProvider timeProvider) : ITeamInvitationService
{
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    public async Task<TeamInvitationSummary> InviteAsync(
        Guid tenantId,
        Guid callerUserId,
        InviteTeamMemberRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageTeam, cancellationToken);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var now = timeProvider.GetUtcNow();

        var targetUser = await userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (targetUser is null)
        {
            var placeholderHash = passwordHasher.Hash($"{Guid.NewGuid():N}{Guid.NewGuid():N}");
            targetUser = User.CreateInvited(normalizedEmail, DeriveDisplayName(normalizedEmail), placeholderHash, now);
            await userRepository.AddAsync(targetUser, cancellationToken);
        }
        else
        {
            var existingMembership = await unitOfWork.QueryInTenantScopeAsync(
                ct => membershipRepository.GetByTenantAndUserAsync(tenantId, targetUser.Id, ct),
                cancellationToken);
            if (existingMembership is not null)
            {
                throw new MembershipAlreadyExistsException(normalizedEmail);
            }
        }

        var membership = Membership.Invite(tenantId, targetUser.Id, request.Role, callerUserId, now);
        await membershipRepository.AddAsync(membership, cancellationToken);

        var rawToken = GenerateRawToken();
        var invitationToken = InvitationToken.Issue(membership.Id, tenantId, Hash(rawToken), now, InvitationLifetime);
        await invitationTokenRepository.AddAsync(invitationToken, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tenant = await RequireTenantAsync(tenantId, cancellationToken);
        await emailSender.SendAsync(normalizedEmail, tenant.Name, rawToken, cancellationToken);

        return new TeamInvitationSummary(membership.Id, normalizedEmail, membership.Role, now, invitationToken.ExpiresAt);
    }

    public async Task<TeamInvitationSummary> ResendAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageTeam, cancellationToken);
        var membership = await GetPendingInvitationAsync(tenantId, membershipId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        await invitationTokenRepository.InvalidateForMembershipAsync(membershipId, now, cancellationToken);

        var rawToken = GenerateRawToken();
        var invitationToken = InvitationToken.Issue(membershipId, tenantId, Hash(rawToken), now, InvitationLifetime);
        await invitationTokenRepository.AddAsync(invitationToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var targetUser = await RequireUserAsync(membership.UserId, cancellationToken);
        var tenant = await RequireTenantAsync(tenantId, cancellationToken);
        await emailSender.SendAsync(targetUser.Email, tenant.Name, rawToken, cancellationToken);

        return new TeamInvitationSummary(membership.Id, targetUser.Email, membership.Role, membership.InvitedAt ?? now, invitationToken.ExpiresAt);
    }

    public async Task RevokeAsync(Guid tenantId, Guid callerUserId, Guid membershipId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageTeam, cancellationToken);
        var membership = await GetPendingInvitationAsync(tenantId, membershipId, cancellationToken);

        membership.Deactivate();
        await invitationTokenRepository.InvalidateForMembershipAsync(membershipId, timeProvider.GetUtcNow(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<AcceptInvitationResult> AcceptAsync(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var invitationToken = await invitationTokenRepository.GetByTokenHashAsync(Hash(request.Token), cancellationToken);
        if (invitationToken is null || !invitationToken.IsValid(now))
        {
            throw new InvalidInvitationException();
        }

        // The token is the only thing known so far — it carries the tenant so the
        // membership lookup right below can establish that scope before it runs.
        tenantContextSetter.SetTenant(invitationToken.TenantId);
        var membership = await unitOfWork.QueryInTenantScopeAsync(
            ct => membershipRepository.GetByIdAsync(invitationToken.MembershipId, ct),
            cancellationToken);

        if (membership is null || !membership.IsActive || !membership.IsPending)
        {
            throw new InvalidInvitationException();
        }

        var targetUser = await RequireUserAsync(membership.UserId, cancellationToken);

        if (targetUser.PasswordSetAt is null)
        {
            targetUser.SetPassword(passwordHasher.Hash(request.Password), now);
            if (!string.IsNullOrWhiteSpace(request.FullName))
            {
                targetUser.Rename(request.FullName);
            }

            targetUser.VerifyEmail(now);
        }
        else if (!passwordHasher.Verify(targetUser.PasswordHash, request.Password))
        {
            throw new InvalidCredentialsException();
        }

        membership.Accept(now);
        invitationToken.MarkUsed(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tenant = await RequireTenantAsync(invitationToken.TenantId, cancellationToken);

        return new AcceptInvitationResult(tenant.Id, tenant.Slug, targetUser.Id, targetUser.Email, targetUser.FullName);
    }

    private async Task<Membership> GetPendingInvitationAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
    {
        var membership = await unitOfWork.QueryInTenantScopeAsync(
            ct => membershipRepository.GetByIdAsync(membershipId, ct),
            cancellationToken);

        // TenantId is re-checked even though the query filter already scoped the
        // lookup to tenantId — cheap defense in depth against a future filter bug.
        if (membership is null || membership.TenantId != tenantId || !membership.IsPending)
        {
            throw new InvitationNotFoundException();
        }

        return membership;
    }

    private async Task<Tenant> RequireTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => await tenantRepository.GetByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found.");

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken cancellationToken)
        => await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{userId}' not found.");

    private static string DeriveDisplayName(string email) => email[..email.IndexOf('@')];

    private static string GenerateRawToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
