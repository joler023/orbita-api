using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(m => m.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(m => m.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(m => m.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.InvitedBy).HasColumnName("invited_by");
        builder.Property(m => m.InvitedAt).HasColumnName("invited_at");
        builder.Property(m => m.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(m => m.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();

        // tenant_id first, per the isolation rule in orbita-schema.dbml.
        builder.HasIndex(m => new { m.TenantId, m.UserId })
            .IsUnique()
            .HasDatabaseName("ux_memberships_tenant_user");
        builder.HasIndex(m => m.UserId);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
