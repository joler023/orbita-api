using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Domain.Tenants;
using Orbita.Infrastructure.Persistence;
using Orbita.Infrastructure.Persistence.Repositories;

namespace Orbita.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrbitaInfrastructure(this IServiceCollection services)
    {
        // The connection string is resolved from IConfiguration lazily, inside the
        // options delegate, rather than captured once up front: WebApplicationFactory
        // (used by Orbita.IntegrationTests) layers its test overrides onto
        // IConfiguration only by the time the host finishes building, which is after
        // Program.cs's own top-level statements have already run. Reading it eagerly
        // here would silently keep using whatever ConnectionStrings:Postgres was at
        // registration time — appsettings.Development.json's real local database —
        // instead of the ephemeral one the test actually wired up.
        services.AddDbContext<OrbitaDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Postgres' configuration value.");
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<ITenantRepository, TenantRepository>();

        return services;
    }
}
