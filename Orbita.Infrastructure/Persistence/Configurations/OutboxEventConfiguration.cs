using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Outbox;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>
/// No RLS/query filter, deliberately: <c>Orbita.Application.Outbox.OutboxDispatchService</c>
/// reads across every tenant's pending events with no ambient tenant set — same
/// category as <c>channel_accounts</c>/<c>inbound_webhook_events</c>.
/// </summary>
public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityByDefaultColumn();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.AggregateType).HasColumnName("aggregate_type").HasMaxLength(OutboxEvent.AggregateTypeMaxLength).IsRequired();
        builder.Property(e => e.AggregateId).HasColumnName("aggregate_id").IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(OutboxEvent.EventTypeMaxLength).IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.TraceId).HasColumnName("trace_id");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.PublishedAt).HasColumnName("published_at");
        builder.Property(e => e.Attempts).HasColumnName("attempts").IsRequired();

        builder.HasIndex(e => e.PublishedAt).HasDatabaseName("ix_outbox_pending").HasFilter("published_at IS NULL");
        builder.HasIndex(e => new { e.AggregateType, e.AggregateId });
    }
}
