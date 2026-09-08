using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Channels;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps inbound_webhook_events for migrations and reads only — writes go through raw
/// SQL (<see cref="Channels.PostgresInboundWebhookQueue"/>) to get
/// ON CONFLICT DO NOTHING, so this entity is never added through the change tracker.
/// No RLS/query filter: same reasoning as channel_accounts (ORB-B01) — an inbound
/// webhook is routed to a tenant, not scoped by one that's already known.
/// </summary>
public sealed class InboundWebhookEventConfiguration : IEntityTypeConfiguration<InboundWebhookEvent>
{
    public void Configure(EntityTypeBuilder<InboundWebhookEvent> builder)
    {
        builder.ToTable("inbound_webhook_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityByDefaultColumn();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.ChannelAccountId).HasColumnName("channel_account_id").IsRequired();
        builder.Property(e => e.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.PayloadHash).HasColumnName("payload_hash").HasMaxLength(InboundWebhookEvent.PayloadHashLength).IsRequired();
        builder.Property(e => e.ReceivedAt).HasColumnName("received_at").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(e => e.LastError).HasColumnName("last_error");
        builder.Property(e => e.LockedAt).HasColumnName("locked_at");
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");

        builder.HasIndex(e => new { e.Status, e.Id });
        builder.HasIndex(e => new { e.TenantId, e.Status, e.ReceivedAt });
        builder.HasIndex(e => e.PayloadHash).IsUnique();
    }
}
