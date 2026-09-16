using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class RoutingRuleConfiguration : IEntityTypeConfiguration<RoutingRule>
{
    public void Configure(EntityTypeBuilder<RoutingRule> builder)
    {
        builder.ToTable("routing_rules");

        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(rule => rule.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(rule => rule.Position).HasColumnName("position").IsRequired();
        builder.Property(rule => rule.Name).HasColumnName("name").HasMaxLength(RoutingRule.NameMaxLength).IsRequired();
        builder.Property(rule => rule.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.Keyword).HasColumnName("keyword").HasMaxLength(RoutingRule.KeywordMaxLength);
        builder.Property(rule => rule.AgentId).HasColumnName("agent_id");
        builder.Property(rule => rule.CreatedAt).HasColumnName("created_at").IsRequired();

        // tenant_id first, per the isolation rule — and in evaluation order, which is the
        // only way this table is ever read.
        builder.HasIndex(rule => new { rule.TenantId, rule.Position }).HasDatabaseName("ix_routing_rules_tenant_position");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(rule => rule.TenantId).OnDelete(DeleteBehavior.Cascade);

        // Deleting an assistant a rule points at is refused by the FK rather than leaving a
        // rule that routes to nothing — the same stance ai_runs and conversations take.
        builder.HasOne<AiAgent>().WithMany().HasForeignKey(rule => rule.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}
