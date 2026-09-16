using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Orbita.Application.Ai;
using Orbita.Application.Billing;
using Orbita.Application.Channels;
using Orbita.Application.Identity;
using Orbita.Application.Outbox;
using Orbita.Domain.Billing;
using Orbita.Application.Media;
using Orbita.Infrastructure.Media;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.Ai;
using Orbita.IntegrationTests.TestSupport;
using Testcontainers.PostgreSql;
using Xunit;

namespace Orbita.IntegrationTests;

public sealed class TenantsApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Matches the migration's bootstrap of a least-privileged runtime role: the
    // container's own admin user owns the schema and runs migrations, and the app
    // under test connects as `orbita_app` instead, so Row Level Security actually
    // applies to it — Postgres never enforces RLS against a table owner or a
    // superuser, so testing isolation through the admin connection would prove
    // nothing (see ORB-A09 / TenantIsolationTests).
    private const string AppRoleUsername = "orbita_app";
    private const string AppRolePassword = "orbita_app_dev_only";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("orbita_test")
        .WithUsername("orbita")
        .WithPassword("orbita")
        .Build();

    /// <summary>
    /// No real email provider is wired up yet (ORB-A07) — this replaces the
    /// logging-only sender so a test can get at the raw invitation token that would
    /// otherwise only ever exist inside an email nobody sends.
    /// </summary>
    public CapturingInvitationEmailSender InvitationEmails { get; } = new();

    /// <summary>Same idea as <see cref="InvitationEmails"/>, for password reset links (ORB-A10).</summary>
    public CapturingPasswordResetEmailSender PasswordResetEmails { get; } = new();

    /// <summary>Records the webhook subscriptions the backend would have asked Meta for (ORB-B01).</summary>
    public FakeWhatsAppCloudApiClient WhatsAppApi { get; } = new();

    /// <summary>Records every outbox event OutboxDispatcherWorker actually publishes (ORB-B04).</summary>
    public RecordingIntegrationEventHandler IntegrationEvents { get; } = new();

    /// <summary>Public base URL the app under test believes Meta can reach it at — see ConfigureWebHost.</summary>
    public const string WebhookPublicBaseUrl = "https://api.orbita.test";

    /// <summary>The app-level hub.verify_token configured for the test host.</summary>
    public const string GlobalVerifyToken = "test-global-verify-token";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<OrbitaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options;
        await using var migrationContext = new OrbitaDbContext(adminOptions, new AmbientTenantContext());
        await migrationContext.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = BuildAppConnectionString(),
                ["Channels:Meta:AppId"] = "test-app-id",
                ["Channels:Meta:AppSecret"] = "test-app-secret",
                ["Channels:Meta:VerifyToken"] = GlobalVerifyToken,
                ["Channels:Webhooks:PublicBaseUrl"] = WebhookPublicBaseUrl,
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInvitationEmailSender>();
            services.AddSingleton<IInvitationEmailSender>(InvitationEmails);
            services.RemoveAll<IPasswordResetEmailSender>();
            services.AddSingleton<IPasswordResetEmailSender>(PasswordResetEmails);

            // Neither payment rail has real credentials in tests — hitting them would
            // mean live network calls to Stripe/Wompi. See FakePaymentProvider.
            services.RemoveAll<IPaymentProvider>();
            services.AddSingleton<IPaymentProvider>(new FakePaymentProvider(PaymentProvider.Stripe));
            services.AddSingleton<IPaymentProvider>(new FakePaymentProvider(PaymentProvider.Wompi));

            // No Meta app exists either (ORB-B01) — the Graph API clients are replaced
            // with fakes the same way; the real ones would need Channels:Meta:* filled in.
            services.RemoveAll<IMetaAuthClient>();
            services.AddSingleton<IMetaAuthClient>(new FakeMetaAuthClient());
            services.RemoveAll<IWhatsAppCloudApiClient>();
            services.AddSingleton<IWhatsAppCloudApiClient>(WhatsAppApi);

            services.AddSingleton<IIntegrationEventHandler>(IntegrationEvents);

            // Same reasoning for the model provider: a real one would need Ollama running
            // or an OpenRouter balance, and would make every assertion flaky.
            services.RemoveAll<ILlmProvider>();
            services.AddSingleton<ILlmProvider>(Llm);

            // The background indexer is removed so tests decide when indexing happens:
            // left running, it would race every assertion about a document's status.
            //
            // Only that one. This used to be `RemoveAll<IHostedService>()`, which also
            // took out ORB-B03's InboundMessageWorker, ORB-B04's OutboxDispatcherWorker,
            // ORB-B05's OutboundMessageWorker and ORB-B01's ChannelTokenExpiryWorker —
            // so every test that waits for a background worker waited for something that
            // was never going to run, and sixteen of them failed on a timeout that said
            // nothing about the cause. Nobody saw it because these tests had never been
            // executed: the machine they were written on had no Docker.
            services.Remove(services.Single(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(Orbita.Infrastructure.Ai.KnowledgeIndexingHostedService)));

            // Uploaded files go to a directory this fixture owns, instead of the shared
            // media root. Same storage implementation as production (ORB-B06's), only
            // pointed somewhere disposable.
            services.RemoveAll<IMediaStorage>();
            services.AddSingleton<IMediaStorage>(
                new LocalFileMediaStorage(
                    Microsoft.Extensions.Options.Options.Create(
                        new MediaOptions { LocalStoragePath = KnowledgeStorageRoot })));
        });
    }

    /// <summary>
    /// The container's own admin/table-owner connection string — a superuser, so RLS
    /// never applies to it. Exposed so tests can isolate the EF Core query filter from
    /// Row Level Security by deliberately bypassing the latter (see
    /// TenantIsolationTests.QueryFilter_OnlyReturnsMembershipsForTheAmbientTenant).
    /// </summary>
    public string GetAdminConnectionString() => _postgres.GetConnectionString();

    /// <summary>
    /// A DbContext connected as the table owner, so Row Level Security does not apply.
    ///
    /// This is what a test uses to check that a background worker did its job. Reading
    /// those tables through the app's own role from a test scope answers *nothing* — not
    /// an error, zero rows — because `app.tenant_id` is set with SET LOCAL semantics
    /// inside the transactions the app itself opens, and a test scope has none. Sixteen
    /// tests failed on exactly that, asserting through a connection that could never see
    /// what the worker had just written.
    ///
    /// Bypassing RLS here proves nothing about isolation and is not meant to:
    /// <see cref="TenantIsolationTests"/> is where isolation is proven, on purpose,
    /// through the app's own role.
    /// </summary>
    public OrbitaDbContext CreateOwnerDbContext(Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<OrbitaDbContext>()
            // UseVector, like the production registration: without it EF cannot map
            // knowledge_chunks.embedding and the whole model fails to build.
            .UseNpgsql(GetAdminConnectionString(), npgsql => npgsql.UseVector())
            .Options;

        var tenantContext = new AmbientTenantContext();

        if (tenantId is { } id)
        {
            tenantContext.SetTenant(id);
        }

        return new OrbitaDbContext(options, tenantContext);
    }

    /// <summary>The stand-in model provider; tests can script a failure on it.</summary>
    public FakeLlmProvider Llm { get; } = new();

    /// <summary>Where ORB-C02 uploads land during tests. Removed on teardown.</summary>
    public string KnowledgeStorageRoot { get; } =
        Path.Combine(Path.GetTempPath(), "orbita-tests", Guid.NewGuid().ToString("N"));

    private string BuildAppConnectionString()
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleUsername,
            Password = AppRolePassword,
        };
        return connectionStringBuilder.ConnectionString;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
