using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Domain.Tenants;
using Orbita.Infrastructure.Persistence;
using Orbita.Infrastructure.Persistence.Repositories;

namespace Orbita.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrbitaInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Postgres' configuration value.");

        services.AddDbContext<OrbitaDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITenantRepository, TenantRepository>();

        return services;
    }
}
