using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class AiAgentConfiguration : IEntityTypeConfiguration<AiAgent>
{
    public void Configure(EntityTypeBuilder<AiAgent> builder)
    {
        builder.ToTable("ai_agents");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(a => a.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(a => a.SystemPrompt).HasColumnName("system_prompt").IsRequired();
        builder.Property(a => a.Temperature).HasColumnName("temperature").HasPrecision(3, 2).IsRequired();
        builder.Property(a => a.MaxTokens).HasColumnName("max_tokens").IsRequired();
        builder.Property(a => a.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        // tenant_id first, per the isolation rule in orbita-schema.dbml.
        builder.HasIndex(a => new { a.TenantId, a.IsEnabled }).HasDatabaseName("ix_ai_agents_tenant_enabled");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
