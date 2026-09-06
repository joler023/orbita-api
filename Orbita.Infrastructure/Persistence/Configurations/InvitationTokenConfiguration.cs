using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Identity;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>
/// Not tenant-scoped itself (same reasoning as RefreshTokenConfiguration): it is
/// looked up by its hash before any tenant is known. TenantId is a plain denormalized
/// column, not an isolation boundary here.
/// </summary>
public sealed class InvitationTokenConfiguration : IEntityTypeConfiguration<InvitationToken>
{
    public void Configure(EntityTypeBuilder<InvitationToken> builder)
    {
        builder.ToTable("invitation_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.MembershipId).HasColumnName("membership_id").IsRequired();
        builder.Property(t => t.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.UsedAt).HasColumnName("used_at");

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.MembershipId);

        // No EF-configured relationship to Membership: Membership carries the
        // tenant isolation query filter, and EF Core rejects a required
        // relationship from an unfiltered entity to a filtered one at model-build
        // time. MembershipId stays a plain, indexed column instead — Memberships
        // are deactivated, not deleted, so there is no dangling-reference concern
        // in practice.
    }
}
