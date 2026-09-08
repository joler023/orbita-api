using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Inbox;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>
/// No RLS/query filter: the dispatcher worker scans across every tenant's due jobs with
/// no ambient tenant set (same category as inbound_webhook_events/outbox_events) —
/// unlike messages itself, which IS RLS'd, this table only ever holds send-attempt
/// bookkeeping, never message content.
/// </summary>
public sealed class OutboundMessageJobConfiguration : IEntityTypeConfiguration<OutboundMessageJob>
{
    public void Configure(EntityTypeBuilder<OutboundMessageJob> builder)
    {
        builder.ToTable("outbound_message_jobs");

        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).HasColumnName("id").UseIdentityByDefaultColumn();

        builder.Property(j => j.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(j => j.MessageId).HasColumnName("message_id").IsRequired();
        builder.Property(j => j.MessageCreatedAt).HasColumnName("message_created_at").IsRequired();
        builder.Property(j => j.ChannelAccountId).HasColumnName("channel_account_id").IsRequired();
        builder.Property(j => j.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(j => j.NextAttemptAt).HasColumnName("next_attempt_at").IsRequired();
        builder.Property(j => j.LastError).HasColumnName("last_error").HasMaxLength(OutboundMessageJob.LastErrorMaxLength);
        builder.Property(j => j.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(j => new { j.Status, j.NextAttemptAt });
        builder.HasIndex(j => j.ChannelAccountId);
    }
}
