using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Ai;
using Orbita.Application.Audit;
using Orbita.Application.Billing;
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
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<ISubscriptionWebhookService, SubscriptionWebhookService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<ITenantSettingsService, TenantSettingsService>();

        // ORB-C02. The chunker is stateless, hence a singleton; everything else follows
        // the unit of work's scope.
        services.AddSingleton<ITextChunker, TextChunker>();
        services.AddScoped<IAiRunRecorder, AiRunRecorder>();
        services.AddScoped<IDocumentChunkBuilder, DocumentChunkBuilder>();
        services.AddScoped<IKnowledgeIndexer, KnowledgeIndexer>();
        services.AddScoped<IKnowledgeDocumentService, KnowledgeDocumentService>();
        services.AddScoped<IKnowledgeSearchService, KnowledgeSearchService>();
        services.AddScoped<IAiAgentService, AiAgentService>();

        return services;
    }
}
