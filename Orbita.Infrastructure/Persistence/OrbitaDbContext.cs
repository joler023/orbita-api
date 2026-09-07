using Microsoft.EntityFrameworkCore;
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
    }
}
