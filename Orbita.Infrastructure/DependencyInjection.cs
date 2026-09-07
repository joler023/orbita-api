using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Ai;
using Orbita.Application.Common;
using Orbita.Infrastructure.Ai;
using Orbita.Domain.Audit;
using Orbita.Infrastructure.Common;
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
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ITwoFactorBackupCodeRepository, TwoFactorBackupCodeRepository>();
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

        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext, HttpRequestContext>();

        return services;
    }

    /// <summary>
    /// ORB-C01. Both adapters are registered under their own concrete types, and
    /// <see cref="ILlmProvider"/> — what the rest of the application actually resolves —
    /// is the <see cref="ResilientLlmProvider"/> wrapping them in failover order.
    /// Registering the adapters as <c>ILlmProvider</c> too would make the wrapper
    /// resolve itself.
    ///
    /// Order is Ollama first, then the OpenAI-compatible endpoint: the local model
    /// costs nothing and works offline, so it is the sensible primary while no paid
    /// account exists. Flipping that once a hosted provider is configured is a one-line
    /// change here — deliberately not a configuration knob, since nothing in the
    /// backlog asks for one.
    ///
    /// Both read their configuration inside the client-configuration delegate rather
    /// than at registration time, for the same reason the connection string does.
    /// </summary>
    private static void AddLlmProviders(IServiceCollection services)
    {
        services.AddSingleton<ILlmModelSelector, ConfigurationLlmModelSelector>();

        services.AddHttpClient<OllamaLlmProvider>((serviceProvider, client) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            // Defaulting to Ollama's standard local port means a developer who has it
            // installed gets a working provider with no configuration at all.
            var baseUrl = configuration["Ai:Providers:Ollama:BaseUrl"] ?? "http://localhost:11434";

            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            }

            client.Timeout = LlmRequestTimeout;
        });

        // A *named* client rather than a typed one: this provider's constructor also
        // takes its pricing, which the container has no way to supply, so the instance
        // is built by the factory below.
        services.AddHttpClient(OpenAiCompatibleClientName, (serviceProvider, client) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var baseUrl = configuration["Ai:Providers:OpenAiCompatible:BaseUrl"];

            // Empty by default: the provider reports IsConfigured = false and the
            // resilience layer skips it, exactly like Stripe/Wompi without credentials.
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            }

            var apiKey = configuration["Ai:Providers:OpenAiCompatible:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            client.Timeout = LlmRequestTimeout;
        });

        services.AddScoped<OpenAiCompatibleLlmProvider>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(OpenAiCompatibleClientName);

            return new OpenAiCompatibleLlmProvider(
                httpClient,
                ReadPrice(configuration, "Ai:Providers:OpenAiCompatible:UsdPerMillionInputTokens"),
                ReadPrice(configuration, "Ai:Providers:OpenAiCompatible:UsdPerMillionOutputTokens"));
        });

        services.AddScoped<ILlmProvider>(serviceProvider => new ResilientLlmProvider(
        [
            serviceProvider.GetRequiredService<OllamaLlmProvider>(),
            serviceProvider.GetRequiredService<OpenAiCompatibleLlmProvider>(),
        ]));
    }

    private const string OpenAiCompatibleClientName = "openai-compatible";

    /// <summary>Generation is slow; the default 100-second HttpClient timeout cuts long replies off.</summary>
    private static readonly TimeSpan LlmRequestTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Zero when unset, which is correct for a locally hosted model — see
    /// <see cref="OpenAiCompatibleLlmProvider"/> on why this must be filled in before
    /// pointing at a paid endpoint.
    /// </summary>
    private static decimal ReadPrice(IConfiguration configuration, string key)
        => decimal.TryParse(configuration[key], CultureInfo.InvariantCulture, out var price) ? price : 0m;
}
