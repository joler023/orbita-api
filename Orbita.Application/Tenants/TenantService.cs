using Orbita.Domain.Tenants;

namespace Orbita.Application.Tenants;

public sealed class TenantService(ITenantRepository repository, TimeProvider timeProvider) : ITenantService
{
    public async Task<TenantDto> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        if (await repository.SlugExistsAsync(request.Slug, cancellationToken))
        {
            throw new TenantSlugAlreadyExistsException(request.Slug);
        }

        var now = timeProvider.GetUtcNow();
        var tenant = Tenant.Create(
            request.Slug,
            request.Name,
            request.CountryCode ?? "CO",
            request.Timezone ?? "America/Bogota",
            request.Locale ?? "es-CO",
            now);

        await repository.AddAsync(tenant, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return ToDto(tenant);
    }

    public async Task<TenantDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenant = await repository.GetByIdAsync(id, cancellationToken);
        return tenant is null ? null : ToDto(tenant);
    }

    private static TenantDto ToDto(Tenant tenant) => new(
        tenant.Id,
        tenant.Slug,
        tenant.Name,
        tenant.CountryCode,
        tenant.Timezone,
        tenant.Locale,
        tenant.IsActive,
        tenant.CreatedAt,
        tenant.UpdatedAt);
}
