using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Audit;
using Orbita.Application.Billing;
using Orbita.Application.Channels;
using Orbita.Application.Crm;
using Orbita.Application.Identity;
using Orbita.Application.Inbox;
using Orbita.Application.Outbox;
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
        services.AddScoped<IWhatsAppChannelService, WhatsAppChannelService>();
        services.AddScoped<IChannelWebhookVerificationService, ChannelWebhookVerificationService>();
        services.AddScoped<IChannelTokenExpiryService, ChannelTokenExpiryService>();
        services.AddScoped<IWebhookIngestionService, WebhookIngestionService>();
        services.AddScoped<IInboundMessageProcessor, InboundMessageProcessor>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IOutboxDispatchService, OutboxDispatchService>();
        services.AddScoped<IOutboundMessageService, OutboundMessageService>();
        services.AddScoped<IOutboundMessageDispatchService, OutboundMessageDispatchService>();
        services.AddScoped<IPipelineService, PipelineService>();
        services.AddScoped<IOpportunityService, OpportunityService>();
        services.AddScoped<IContactService, ContactService>();

        return services;
    }
}
