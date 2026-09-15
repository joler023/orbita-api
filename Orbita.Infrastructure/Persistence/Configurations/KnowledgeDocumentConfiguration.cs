using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_docs");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(d => d.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(d => d.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(d => d.Title).HasColumnName("title").HasMaxLength(300).IsRequired();

        builder.Property(d => d.SourceType)
            .HasColumnName("source_type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(d => d.SourceRef).HasColumnName("source_ref");

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.ChunkCount).HasColumnName("chunk_count").IsRequired();

        // Not in orbita-schema.dbml — see KnowledgeDocument.FailureReason for why it
        // exists. Sized generously because it is prose shown to a user, not a code.
        builder.Property(d => d.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);

        builder.Property(d => d.IndexedAt).HasColumnName("indexed_at");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(d => new { d.TenantId, d.AgentId, d.CreatedAt })
            .HasDatabaseName("ix_knowledge_docs_tenant_agent_created");

        // The indexer's "what is pending here" query, which runs on every pass.
        builder.HasIndex(d => new { d.TenantId, d.Status })
            .HasDatabaseName("ix_knowledge_docs_tenant_status");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AiAgent>().WithMany().HasForeignKey(d => d.AgentId).OnDelete(DeleteBehavior.Cascade);
    }
}
