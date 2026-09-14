using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeIndexingQueueEntryConfiguration : IEntityTypeConfiguration<KnowledgeIndexingQueueEntry>
{
    public void Configure(EntityTypeBuilder<KnowledgeIndexingQueueEntry> builder)
    {
        builder.ToTable("knowledge_indexing_queue");

        // Keyed by the document's own id, so enqueueing twice is idempotent.
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("doc_id").ValueGeneratedNever();
        builder.Ignore(e => e.DocumentId);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.EnqueuedAt).HasColumnName("enqueued_at").IsRequired();

        // The worker's only query: oldest first, no tenant predicate — see the entity's
        // comment on why this table is deliberately not tenant-scoped.
        builder.HasIndex(e => e.EnqueuedAt).HasDatabaseName("ix_knowledge_indexing_queue_enqueued");

        builder.HasOne<KnowledgeDocument>().WithMany().HasForeignKey(e => e.Id).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
