using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Audit;
using Orbita.Application.Identity;
using Orbita.Application.Tenants;

namespace Orbita.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOrbitaApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IOrganizationRegistrationService, OrganizationRegistrationService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<ITenantAuthorizationService, TenantAuthorizationService>();
        services.AddScoped<ITeamInvitationService, TeamInvitationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<ITeamMembersService, TeamMembersService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();

        return services;
    }
}
