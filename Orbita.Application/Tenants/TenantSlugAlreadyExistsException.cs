namespace Orbita.Application.Tenants;

public sealed class TenantSlugAlreadyExistsException(string slug)
    : Exception($"A tenant with slug '{slug}' already exists.")
{
    public string Slug { get; } = slug;
}
