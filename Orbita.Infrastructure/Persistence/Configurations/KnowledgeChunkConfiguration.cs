using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;
using Pgvector;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunks");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(c => c.DocumentId).HasColumnName("doc_id").IsRequired();
        builder.Property(c => c.ChunkIndex).HasColumnName("chunk_index").IsRequired();
        builder.Property(c => c.Content).HasColumnName("content").IsRequired();
        builder.Property(c => c.TokenCount).HasColumnName("token_count");

        // The domain holds a plain float[] so Orbita.Domain stays free of any framework
        // dependency (CLAUDE.md's layering rule); Pgvector's Vector is a persistence
        // concern and lives only on this side of the boundary.
        builder.Property(c => c.Embedding)
            .HasColumnName("embedding")
            .HasColumnType($"vector({KnowledgeChunk.EmbeddingDimensions})")
            .HasConversion(
                embedding => new Vector(embedding),
                vector => vector.ToArray(),
                // Without an explicit comparer EF compares float[] by reference, so it
                // cannot tell whether a loaded embedding changed. Chunks are immutable
                // today, which makes this harmless — but the default would silently stop
                // being harmless the day something does mutate one.
                new ValueComparer<float[]>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    embedding => embedding.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode())),
                    embedding => embedding.ToArray()))
            .IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.DocumentId })
            .HasDatabaseName("ix_knowledge_chunks_tenant_doc");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<KnowledgeDocument>().WithMany().HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}
