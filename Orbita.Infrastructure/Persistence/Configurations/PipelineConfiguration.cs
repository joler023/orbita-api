using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Crm;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class PipelineConfiguration : IEntityTypeConfiguration<Pipeline>
{
    public void Configure(EntityTypeBuilder<Pipeline> builder)
    {
        builder.ToTable("pipelines");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(Pipeline.NameMaxLength)
            .IsRequired();
        builder.Property(p => p.IsDefault).HasColumnName("is_default").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.Name }).HasDatabaseName("ix_pipelines_tenant_name");
        builder.HasIndex(p => new { p.TenantId, p.IsDefault }).HasDatabaseName("ix_pipelines_tenant_default");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
