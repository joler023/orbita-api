using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Audit;
using Orbita.Domain.Billing;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;
using Orbita.Domain.Inbox;
using Orbita.Domain.Outbox;
using Orbita.Domain.Tenants;
using Orbita.Infrastructure.Channels;

namespace Orbita.Infrastructure.Persistence;

public sealed class OrbitaDbContext(DbContextOptions<OrbitaDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<InvitationToken> InvitationTokens => Set<InvitationToken>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<TwoFactorBackupCode> TwoFactorBackupCodes => Set<TwoFactorBackupCode>();

    /// <summary>No query filter on purpose — see ChannelAccount (resolved from a webhook before any tenant is known).</summary>
    public DbSet<ChannelAccount> ChannelAccounts => Set<ChannelAccount>();

    /// <summary>Infrastructure-only stand-in for Secrets Manager — see DataProtectionChannelCredentialStore.</summary>
    public DbSet<ChannelCredential> ChannelCredentials => Set<ChannelCredential>();

    /// <summary>
    /// No query filter on purpose — see ChannelAccount. Written through raw SQL (see
    /// PostgresInboundWebhookQueue), never through this DbSet's change tracker.
    /// </summary>
    public DbSet<InboundWebhookEvent> InboundWebhookEvents => Set<InboundWebhookEvent>();

    public DbSet<Pipeline> Pipelines => Set<Pipeline>();

    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();

    public DbSet<Opportunity> Opportunities => Set<Opportunity>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<ContactFieldDefinition> ContactFieldDefinitions => Set<ContactFieldDefinition>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    /// <summary>No query filter — the dispatcher reads across every tenant's pending events. See OutboxEventConfiguration.</summary>
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrbitaDbContext).Assembly);

        // Layer 1 of the isolation rule in orbita-schema.dbml (layer 2 is the RLS
        // policy applied in the migration; layer 3 is the tenant_id-first index in
        // MembershipConfiguration). A null ambient tenant does not filter — it is the
        // escape hatch for pre-auth flows like registration, which set the tenant
        // explicitly instead. `tenantContext` is captured by reference on purpose: EF
        // Core re-evaluates the filter per query, so a value set later in the same
        // scoped context (see ITenantContextSetter) is picked up without recreating
        // the DbContext.
        modelBuilder.Entity<Membership>()
            .HasQueryFilter(m => tenantContext.TenantId == null || m.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<AuditLogEntry>()
            .HasQueryFilter(e => tenantContext.TenantId == null || e.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<Pipeline>()
            .HasQueryFilter(p => tenantContext.TenantId == null || p.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<PipelineStage>()
            .HasQueryFilter(s => tenantContext.TenantId == null || s.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<Opportunity>()
            .HasQueryFilter(o => tenantContext.TenantId == null || o.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<Contact>()
            .HasQueryFilter(c => tenantContext.TenantId == null || c.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<ContactFieldDefinition>()
            .HasQueryFilter(f => tenantContext.TenantId == null || f.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<Conversation>()
            .HasQueryFilter(c => tenantContext.TenantId == null || c.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<Message>()
            .HasQueryFilter(m => tenantContext.TenantId == null || m.TenantId == tenantContext.TenantId);
    }
}
