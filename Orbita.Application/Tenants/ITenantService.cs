namespace Orbita.Application.Tenants;

public interface ITenantService
{
    /// <exception cref="TenantSlugAlreadyExistsException">The slug is already taken.</exception>
    Task<TenantDto> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken);

    Task<TenantDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
