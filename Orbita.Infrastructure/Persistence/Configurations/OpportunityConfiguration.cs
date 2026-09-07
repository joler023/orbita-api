using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Crm;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> builder)
    {
        builder.ToTable("opportunities");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(o => o.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(o => o.PipelineId).HasColumnName("pipeline_id").IsRequired();
        builder.Property(o => o.StageId).HasColumnName("stage_id").IsRequired();
        builder.Property(o => o.Title)
            .HasColumnName("title")
            .HasMaxLength(Opportunity.TitleMaxLength)
            .IsRequired();
        builder.Property(o => o.Amount).HasColumnName("amount").HasPrecision(18, 2);
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(o => new { o.TenantId, o.StageId }).HasDatabaseName("ix_opportunities_tenant_stage");
        builder.HasIndex(o => new { o.TenantId, o.PipelineId }).HasDatabaseName("ix_opportunities_tenant_pipeline");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(o => o.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Pipeline>().WithMany().HasForeignKey(o => o.PipelineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PipelineStage>().WithMany().HasForeignKey(o => o.StageId).OnDelete(DeleteBehavior.Restrict);
    }
}
