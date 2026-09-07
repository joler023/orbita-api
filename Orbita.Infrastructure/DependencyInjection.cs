using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Billing;
using Orbita.Application.Identity;
using Orbita.Domain.Billing;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;
using Orbita.Infrastructure.Billing;
using Orbita.Infrastructure.Identity;
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

        services.AddScoped<AmbientTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IInvitationTokenRepository, InvitationTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IInvitationEmailSender, LoggingInvitationEmailSender>();
        services.AddSingleton<IPasswordResetEmailSender, LoggingPasswordResetEmailSender>();

        // Both payment rails are registered under the same IPaymentProvider interface
        // — SubscriptionService resolves the right one from IEnumerable<IPaymentProvider>
        // by Kind. Neither has real credentials configured yet (see appsettings'
        // empty Billing:Stripe/Billing:Wompi sections and CLAUDE.md's Billing section).
        services.AddScoped<IPaymentProvider, StripePaymentProvider>();
        services.AddHttpClient<WompiPaymentProvider>();
        services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<WompiPaymentProvider>());

        return services;
    }
}
