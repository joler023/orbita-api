using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Crm;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class PipelineStageConfiguration : IEntityTypeConfiguration<PipelineStage>
{
    public void Configure(EntityTypeBuilder<PipelineStage> builder)
    {
        builder.ToTable("pipeline_stages");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(s => s.PipelineId).HasColumnName("pipeline_id").IsRequired();
        builder.Property(s => s.Name)
            .HasColumnName("name")
            .HasMaxLength(PipelineStage.NameMaxLength)
            .IsRequired();
        builder.Property(s => s.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(s => s.IsWon).HasColumnName("is_won").IsRequired();
        builder.Property(s => s.IsLost).HasColumnName("is_lost").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.PipelineId, s.SortOrder })
            .HasDatabaseName("ix_pipeline_stages_tenant_pipeline_order");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Pipeline>().WithMany().HasForeignKey(s => s.PipelineId).OnDelete(DeleteBehavior.Cascade);
    }
}
