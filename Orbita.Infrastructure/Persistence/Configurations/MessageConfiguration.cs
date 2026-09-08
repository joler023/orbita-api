using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Inbox;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the partitioned <c>messages</c> table. The composite key mirrors the physical
/// requirement that a partitioned table's primary key include the partition column
/// (<see cref="Message.CreatedAt"/>) — see the migration for the actual
/// <c>PARTITION BY RANGE</c> DDL, which EF's normal <c>CreateTable</c> can't express.
/// </summary>
public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(m => new { m.Id, m.CreatedAt });
        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(m => m.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(m => m.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(m => m.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(m => m.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Body).HasColumnName("body");
        builder.Property(m => m.MediaKey).HasColumnName("media_key");
        builder.Property(m => m.MediaMime).HasColumnName("media_mime").HasMaxLength(Message.MediaMimeMaxLength);
        builder.Property(m => m.ExternalId).HasColumnName("external_id").HasMaxLength(Message.ExternalIdMaxLength);
        builder.Property(m => m.ReplyToExternalId).HasColumnName("reply_to_external_id").HasMaxLength(Message.ExternalIdMaxLength);
        builder.Property(m => m.TemplateId).HasColumnName("template_id");
        builder.Property(m => m.SentByUserId).HasColumnName("sent_by_user_id");
        builder.Property(m => m.AiRunId).HasColumnName("ai_run_id");
        builder.Property(m => m.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.ErrorCode).HasColumnName("error_code").HasMaxLength(Message.ErrorCodeMaxLength);
        builder.Property(m => m.SentAt).HasColumnName("sent_at");
        builder.Property(m => m.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(m => m.ReadAt).HasColumnName("read_at");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();

        // The real unique constraint on external_id is enforced by a partial unique
        // index created by hand in the migration (a UNIQUE index on a partitioned table
        // must include the partition key, so it can't guarantee global uniqueness by
        // itself) — global idempotency comes from IMessageRepository.FindByExternalIdAsync
        // being checked before every insert, plus a single consumer per channel account.
        builder.HasIndex(m => new { m.TenantId, m.ConversationId, m.CreatedAt }).HasDatabaseName("ix_messages_thread");
        builder.HasIndex(m => new { m.TenantId, m.Category, m.CreatedAt }).HasDatabaseName("ix_messages_billing");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}
