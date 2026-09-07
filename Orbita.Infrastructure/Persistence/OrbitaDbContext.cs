using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;
using Orbita.Domain.Audit;
using Orbita.Domain.Billing;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;

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

    public DbSet<AiAgent> AiAgents => Set<AiAgent>();

    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();

    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();

    public DbSet<AiRun> AiRuns => Set<AiRun>();

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

        // ORB-C02's four tables. Every one carries tenant_id, so every one gets the
        // filter — layer 1 of the isolation rule; the RLS policy in the migration is
        // layer 2 and the tenant_id-first indexes in the configurations are layer 3.
        modelBuilder.Entity<AiAgent>()
            .HasQueryFilter(a => tenantContext.TenantId == null || a.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<KnowledgeDocument>()
            .HasQueryFilter(d => tenantContext.TenantId == null || d.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<KnowledgeChunk>()
            .HasQueryFilter(c => tenantContext.TenantId == null || c.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<AiRun>()
            .HasQueryFilter(r => tenantContext.TenantId == null || r.TenantId == tenantContext.TenantId);
    }
}
