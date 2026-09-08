using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Common;
using Orbita.Domain.Audit;
using Orbita.Infrastructure.Common;
using Orbita.Application.Billing;
using Orbita.Application.Channels;
using Orbita.Application.Identity;
using Orbita.Domain.Billing;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;
using Orbita.Application.Outbox;
using Orbita.Domain.Inbox;
using Orbita.Domain.Outbox;
using Orbita.Domain.Tenants;
using Orbita.Infrastructure.Billing;
using Orbita.Infrastructure.Channels;
using Orbita.Infrastructure.Channels.Meta;
using Orbita.Infrastructure.Identity;
using Orbita.Infrastructure.Outbox;
using Orbita.Infrastructure.Persistence;
using Orbita.Infrastructure.Persistence.Repositories;
using Orbita.Infrastructure.Workers;

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
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ITwoFactorBackupCodeRepository, TwoFactorBackupCodeRepository>();
        services.AddScoped<IPipelineRepository, PipelineRepository>();
        services.AddScoped<IPipelineStageRepository, PipelineStageRepository>();
        services.AddScoped<IOpportunityRepository, OpportunityRepository>();
        services.AddScoped<IContactRepository, ContactRepository>();
        services.AddScoped<IContactFieldDefinitionRepository, ContactFieldDefinitionRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IOutboxEventRepository, OutboxEventRepository>();
        services.AddScoped<IIntegrationEventPublisher, InProcessIntegrationEventPublisher>();
        services.AddHostedService<OutboxDispatcherWorker>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IInvitationEmailSender, LoggingInvitationEmailSender>();
        services.AddSingleton<IPasswordResetEmailSender, LoggingPasswordResetEmailSender>();
        services.AddSingleton<ITotpProvider, OtpTotpProvider>();
        services.AddDataProtection();
        services.AddSingleton<IUserSecretProtector, DataProtectionUserSecretProtector>();

        // Both payment rails are registered under the same IPaymentProvider interface
        // — SubscriptionService resolves the right one from IEnumerable<IPaymentProvider>
        // by Kind. Neither has real credentials configured yet (see appsettings'
        // empty Billing:Stripe/Billing:Wompi sections and CLAUDE.md's Billing section).
        services.AddScoped<IPaymentProvider, StripePaymentProvider>();
        services.AddHttpClient<WompiPaymentProvider>();
        services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<WompiPaymentProvider>());

        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext, HttpRequestContext>();

        AddChannels(services);

        return services;
    }

    /// <summary>ORB-B01: WhatsApp connection. See CLAUDE.md's Channels section for what each stand-in replaces.</summary>
    private static void AddChannels(IServiceCollection services)
    {
        services.AddScoped<IChannelAccountRepository, ChannelAccountRepository>();
        services.AddScoped<IChannelCredentialStore, DataProtectionChannelCredentialStore>();
        services.AddSingleton<IChannelWebhookSettings, ConfigurationChannelWebhookSettings>();

        // ORB-B02: webhook ingestion. MemoryCache-backed dedup is a single-instance fast
        // path only — see MemoryCacheWebhookDeduplicator for why that's still correct.
        services.AddMemoryCache();
        services.AddSingleton<IWebhookSignatureVerifier, MetaWebhookSignatureVerifier>();
        services.AddSingleton<IWebhookDeduplicator, MemoryCacheWebhookDeduplicator>();
        services.AddScoped<IInboundWebhookQueue, PostgresInboundWebhookQueue>();

        // ORB-B03: inbound message normalization.
        services.AddSingleton<IChannelAdapter, WhatsAppChannelAdapter>();
        services.AddHostedService<InboundMessageWorker>();

        // Graph API clients: typed HttpClients, same registration shape as Wompi. The
        // factory's default request/response logging is removed on purpose — it writes
        // the full request URI at Information level, and Meta's OAuth endpoints carry
        // the app secret and access tokens in the query string.
        services.AddHttpClient<MetaAuthClient>().RemoveAllLoggers();
        services.AddScoped<IMetaAuthClient>(sp => sp.GetRequiredService<MetaAuthClient>());
        services.AddHttpClient<WhatsAppCloudApiClient>().RemoveAllLoggers();
        services.AddScoped<IWhatsAppCloudApiClient>(sp => sp.GetRequiredService<WhatsAppCloudApiClient>());

        services.AddOptions<WorkerOptions>().BindConfiguration(WorkerOptions.SectionName);
        services.AddHostedService<ChannelTokenExpiryWorker>();
    }
}
