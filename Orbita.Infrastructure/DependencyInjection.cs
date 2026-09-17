using System.Net.Http.Headers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Ai;
using Orbita.Application.Common;
using Orbita.Application.Media;
using Orbita.Infrastructure.Ai;
using Orbita.Infrastructure.Media;
using Orbita.Domain.Ai;
using Orbita.Domain.Audit;
using Orbita.Infrastructure.Common;
using Orbita.Application.Billing;
using Orbita.Application.Channels;
using Orbita.Application.Identity;
using Orbita.Application.Inbox;
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
            // UseVector wires Npgsql's mapping for pgvector's `vector` type, which
            // knowledge_chunks.embedding needs (ORB-C02/C03).
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector());
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
        services.AddScoped<IMessageTemplateRepository, MessageTemplateRepository>();
        services.AddScoped<IOutboxEventRepository, OutboxEventRepository>();
        services.AddScoped<IIntegrationEventPublisher, InProcessIntegrationEventPublisher>();
        services.AddHostedService<OutboxDispatcherWorker>();
        services.AddScoped<IOutboundMessageQueue, PostgresOutboundMessageQueue>();
        services.AddSingleton<IOutboundMessageRateLimiter, TokenBucketRateLimiter>();
        services.AddHostedService<OutboundMessageWorker>();
        services.AddOptions<MediaOptions>().BindConfiguration(MediaOptions.SectionName);
        services.AddScoped<IMediaStorage, LocalFileMediaStorage>();
        services.AddSingleton<IMediaUrlSigner, MediaUrlSigner>();
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

        AddLlmProviders(services);
        AddKnowledgeBase(services);

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

        // ORB-B03: inbound message normalization. Scoped, not Singleton — ORB-B05's
        // SendTextAsync pulled in IChannelCredentialStore/IWhatsAppCloudApiClient,
        // both Scoped, and a Singleton can't depend on those without becoming a
        // captive dependency.
        services.AddScoped<IChannelAdapter, WhatsAppChannelAdapter>();
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

    /// <summary>
    /// ORB-C01. Both adapters are registered under their own concrete types, and
    /// <see cref="ILlmProvider"/> — what the rest of the application actually resolves —
    /// is the <see cref="ResilientLlmProvider"/> wrapping them in failover order.
    /// Registering the adapters as <c>ILlmProvider</c> too would make the wrapper
    /// resolve itself.
    ///
    /// Order is the OpenAI-compatible endpoint first, then Ollama. The hosted gateway is
    /// the real path: it is what serves the configured models and the only one that can
    /// produce the 1536-dimension embeddings <c>knowledge_chunks</c> stores. Ollama stays
    /// registered as ORB-C01's required second implementation and as chat failover, but
    /// it cannot serve embeddings at this dimension — see
    /// <c>KnowledgeChunk.EmbeddingDimensions</c>.
    ///
    /// An unconfigured provider is skipped rather than tried and failed, so with no API
    /// key the chain simply has one link.
    ///
    /// Both read their configuration inside the client-configuration delegate rather
    /// than at registration time, for the same reason the connection string does.
    /// </summary>
    private static void AddLlmProviders(IServiceCollection services)
    {
        // Scoped, not singleton: it reads the tenant's overrides through the unit of work.
        // The cross-request cache it uses is the singleton IMemoryCache.
        services.AddMemoryCache();
        services.AddScoped<ILlmModelSelector, TenantAwareLlmModelSelector>();
        services.AddSingleton<IModelPreferenceCacheInvalidator, ModelPreferenceCacheInvalidator>();
        services.AddSingleton<ILlmPricing, ConfigurationLlmPricing>();

        services.AddHttpClient<OllamaLlmProvider>((serviceProvider, client) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            // No default: Ollama is opt-in now that the hosted gateway is the real path.
            // Left unset it reports IsConfigured = false and the chain skips it, instead
            // of every call paying a connection timeout to a port nobody is listening on.
            var baseUrl = configuration["Ai:Providers:ollama:BaseUrl"];

            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            }

            client.Timeout = LlmRequestTimeout;
        });

        services.AddHttpClient<OpenAiCompatibleLlmProvider>((serviceProvider, client) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var baseUrl = configuration["Ai:Providers:openai-compatible:BaseUrl"];

            // Empty by default: the provider reports IsConfigured = false and the
            // resilience layer skips it, exactly like Stripe/Wompi without credentials.
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            }

            var apiKey = configuration["Ai:Providers:openai-compatible:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            client.Timeout = LlmRequestTimeout;
        });

        services.AddScoped<ILlmProvider>(serviceProvider => new ResilientLlmProvider(ProvidersInFailoverOrder(serviceProvider)));

        // Built from the same list, so the catalog a screen reads (ORB-C13) is by
        // construction the order a call actually tries.
        services.AddScoped<ILlmProviderCatalog>(serviceProvider => new LlmProviderCatalog(ProvidersInFailoverOrder(serviceProvider)));

        static ILlmProvider[] ProvidersInFailoverOrder(IServiceProvider serviceProvider) =>
        [
            serviceProvider.GetRequiredService<OpenAiCompatibleLlmProvider>(),
            serviceProvider.GetRequiredService<OllamaLlmProvider>(),
        ];
    }

    /// <summary>Generation is slow; the default 100-second HttpClient timeout cuts long replies off.</summary>
    private static readonly TimeSpan LlmRequestTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// ORB-C02. The extractors are registered as a set and picked by extension inside
    /// <c>DocumentChunkBuilder</c> — adding a format is a new class plus a line here,
    /// never an edit to a switch someone has to remember to update.
    /// </summary>
    private static void AddKnowledgeBase(IServiceCollection services)
    {
        services.AddScoped<IAiAgentRepository, AiAgentRepository>();
        services.AddScoped<IAiAgentDraftRepository, AiAgentDraftRepository>();
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
        services.AddScoped<IKnowledgeChunkRepository, KnowledgeChunkRepository>();
        services.AddScoped<IAiRunRepository, AiRunRepository>();
        services.AddScoped<ITenantModelPreferenceRepository, TenantModelPreferenceRepository>();
        services.AddScoped<IRoutingRuleRepository, RoutingRuleRepository>();
        services.AddScoped<IAgentAnswerCacheRepository, AgentAnswerCacheRepository>();
        services.AddScoped<IAgentTestCaseRepository, AgentTestCaseRepository>();
        services.AddScoped<IKnowledgeIndexingQueue, KnowledgeIndexingQueue>();

        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, DocxTextExtractor>();

        // IMediaStorage/MediaOptions are ORB-B06's (Channels) — reused here rather than
        // duplicated, now that both branches have met. See the registration in
        // AddOrbitaInfrastructure above.

        services.AddHostedService<KnowledgeIndexingHostedService>();
    }
}
