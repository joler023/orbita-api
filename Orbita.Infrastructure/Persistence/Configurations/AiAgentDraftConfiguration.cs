using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class AiAgentDraftConfiguration : IEntityTypeConfiguration<AiAgentDraft>
{
    public void Configure(EntityTypeBuilder<AiAgentDraft> builder)
    {
        builder.ToTable("ai_agent_drafts");

        // Keyed by the agent's own id: one pending draft per assistant, so saving twice
        // replaces rather than accumulates.
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("agent_id").ValueGeneratedNever();
        builder.Ignore(d => d.AgentId);

        builder.Property(d => d.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(d => d.Name).HasColumnName("name").HasMaxLength(AiAgent.NameMaxLength).IsRequired();
        builder.Property(d => d.Personality)
            .HasColumnName("personality").HasMaxLength(AiAgent.PersonalityMaxLength).IsRequired();
        builder.Property(d => d.Instructions)
            .HasColumnName("instructions").HasMaxLength(AiAgent.InstructionsMaxLength).IsRequired();

        AgentStyleMapping.Configure(builder, d => d.Style);

        builder.Property<List<string>>("_tools")
            .HasColumnName("tools")
            .HasColumnType("jsonb")
            .HasField("_tools")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // tenant_id first, per the isolation rule. The list screen reads every draft a
        // tenant has in one query, to badge its rows without an N+1.
        builder.HasIndex(d => d.TenantId).HasDatabaseName("ix_ai_agent_drafts_tenant");

        // Deleting an assistant takes its pending draft with it.
        builder.HasOne<AiAgent>().WithMany().HasForeignKey(d => d.Id).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
