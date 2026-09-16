using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class AiRunConfiguration : IEntityTypeConfiguration<AiRun>
{
    public void Configure(EntityTypeBuilder<AiRun> builder)
    {
        builder.ToTable("ai_runs");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(r => r.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(r => r.ConversationId).HasColumnName("conversation_id");
        builder.Property(r => r.Model).HasColumnName("model").HasMaxLength(80).IsRequired();
        builder.Property(r => r.TokensIn).HasColumnName("tokens_in").IsRequired();
        builder.Property(r => r.TokensOut).HasColumnName("tokens_out").IsRequired();

        // numeric(10,6): fractions of a cent matter when a single reply can cost
        // $0.0004 and the bill is the sum of millions of them.
        builder.Property(r => r.CostUsd).HasColumnName("cost_usd").HasPrecision(10, 6).IsRequired();

        builder.Property(r => r.LatencyMs).HasColumnName("latency_ms");
        builder.Property(r => r.FinishReason).HasColumnName("finish_reason").HasMaxLength(40);
        builder.Property(r => r.Error).HasColumnName("error");
        // ORB-C09, both straight from orbita-schema.dbml.
        builder.Property<List<string>>("_toolsCalled")
            .HasColumnName("tools_called")
            .HasColumnType("jsonb")
            .HasField("_toolsCalled")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        builder.Property<List<Guid>>("_retrievedChunkIds")
            .HasColumnName("retrieved_chunk_ids")
            .HasColumnType("uuid[]")
            .HasField("_retrievedChunkIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        // orbita-schema.dbml's was_handoff, left out by ORB-C09 because nothing could set
        // it before ORB-C07 existed.
        builder.Property(r => r.WasHandoff)
            .HasColumnName("was_handoff")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        // The billing rollup's access path (orbita-schema.dbml names this index).
        builder.HasIndex(r => new { r.TenantId, r.CreatedAt }).HasDatabaseName("ix_ai_runs_billing");
        builder.HasIndex(r => r.ConversationId).HasDatabaseName("ix_ai_runs_conversation");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AiAgent>().WithMany().HasForeignKey(r => r.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}
