using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;
using Pgvector;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>
/// ORB-C12's <c>agent_answer_cache</c>. Not in orbita-schema.dbml: it is regenerable by
/// definition — dropping every row costs model calls, never information — which is the
/// DBML's own test for a derived table.
/// </summary>
public sealed class AgentAnswerCacheEntryConfiguration : IEntityTypeConfiguration<AgentAnswerCacheEntry>
{
    public void Configure(EntityTypeBuilder<AgentAnswerCacheEntry> builder)
    {
        builder.ToTable("agent_answer_cache");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(e => e.Question).HasColumnName("question").HasMaxLength(AgentAnswerCacheEntry.QuestionMaxLength).IsRequired();
        builder.Property(e => e.Answer).HasColumnName("answer").IsRequired();
        builder.Property(e => e.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        // Same float[] <-> Vector split as knowledge_chunks.embedding, for the same reason.
        builder.Property(e => e.Embedding)
            .HasColumnName("embedding")
            .HasColumnType($"vector({KnowledgeChunk.EmbeddingDimensions})")
            .HasConversion(
                embedding => new Vector(embedding),
                vector => vector.ToArray(),
                new ValueComparer<float[]>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    embedding => embedding.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode())),
                    embedding => embedding.ToArray()))
            .IsRequired();

        // The lookup narrows by assistant and fingerprint before comparing vectors, so the
        // distance is computed over one configuration's entries, not the whole table.
        builder.HasIndex(e => new { e.TenantId, e.AgentId, e.Fingerprint })
            .HasDatabaseName("ix_agent_answer_cache_tenant_agent_fingerprint");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AiAgent>().WithMany().HasForeignKey(e => e.AgentId).OnDelete(DeleteBehavior.Cascade);
    }
}
