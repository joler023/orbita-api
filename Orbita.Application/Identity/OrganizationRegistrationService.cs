using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;

namespace Orbita.Application.Identity;

/// <summary>
/// Creates a Tenant, its owner User and the Owner Membership that links them, all in
/// one transaction (ORB-A05). The tenant slug is derived from the business name and
/// disambiguated with a numeric suffix on collision — the caller never picks a slug.
/// </summary>
public sealed class OrganizationRegistrationService(
    ITenantRepository tenantRepository,
    IUserRepository userRepository,
    IMembershipRepository membershipRepository,
    IPipelineRepository pipelineRepository,
    IPipelineStageRepository pipelineStageRepository,
    ITenantContextSetter tenantContextSetter,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider) : IOrganizationRegistrationService
{
    private const int MaxSlugAttempts = 999;

    // Reserves room for "-" plus up to a 3-digit suffix so the final candidate never
    // exceeds Tenant.SlugMaxLength.
    private const int MaxBaseSlugLength = Tenant.SlugMaxLength - 4;

    public async Task<RegisterOrganizationResult> RegisterAsync(
        RegisterOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await userRepository.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            throw new EmailAlreadyRegisteredException(normalizedEmail);
        }

        var slug = await GenerateAvailableSlugAsync(request.BusinessName, cancellationToken);
        var now = timeProvider.GetUtcNow();

        var tenant = Tenant.Create(slug, request.BusinessName, now: now);
        var passwordHash = passwordHasher.Hash(request.Password);
        var user = User.Create(normalizedEmail, passwordHash, request.FullName, now);
        var membership = Membership.CreateOwner(tenant.Id, user.Id, now);

        // The membership row is subject to Row Level Security, so the session's
        // acting tenant must be this brand-new tenant before it is inserted.
        tenantContextSetter.SetTenant(tenant.Id);

        var (pipeline, stages) = DefaultSalesPipeline.Create(tenant.Id, now);

        await tenantRepository.AddAsync(tenant, cancellationToken);
        await userRepository.AddAsync(user, cancellationToken);
        await membershipRepository.AddAsync(membership, cancellationToken);
        await pipelineRepository.AddAsync(pipeline, cancellationToken);
        foreach (var stage in stages)
        {
            await pipelineStageRepository.AddAsync(stage, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RegisterOrganizationResult(
            tenant.Id,
            tenant.Slug,
            tenant.Name,
            user.Id,
            user.Email,
            user.FullName);
    }

    private async Task<string> GenerateAvailableSlugAsync(string businessName, CancellationToken cancellationToken)
    {
        var fullSlug = Tenant.Slugify(businessName);
        var baseSlug = fullSlug.Length > MaxBaseSlugLength
            ? fullSlug[..MaxBaseSlugLength].Trim('-')
            : fullSlug;

        if (!await tenantRepository.SlugExistsAsync(baseSlug, cancellationToken))
        {
            return baseSlug;
        }

        for (var suffix = 2; suffix <= MaxSlugAttempts; suffix++)
        {
            var candidate = $"{baseSlug}-{suffix}";
            if (!await tenantRepository.SlugExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not find an available slug derived from '{businessName}' after {MaxSlugAttempts} attempts.");
    }
}
