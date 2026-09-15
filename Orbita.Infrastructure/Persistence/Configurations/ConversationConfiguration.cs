using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Crm;
using Orbita.Domain.Inbox;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(c => c.ContactId).HasColumnName("contact_id").IsRequired();
        builder.Property(c => c.ChannelAccountId).HasColumnName("channel_account_id").IsRequired();
        builder.Property(c => c.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.AssigneeId).HasColumnName("assignee_id");
        builder.Property(c => c.AiAgentId).HasColumnName("ai_agent_id");
        builder.Property(c => c.WindowExpiresAt).HasColumnName("window_expires_at");
        builder.Property(c => c.HumanAgentExpiresAt).HasColumnName("human_agent_expires_at");
        builder.Property(c => c.UnreadCount).HasColumnName("unread_count").IsRequired();
        builder.Property(c => c.LastMessageAt).HasColumnName("last_message_at");
        builder.Property(c => c.LastMessagePreview).HasColumnName("last_message_preview").HasMaxLength(Conversation.LastMessagePreviewMaxLength);
        builder.Property(c => c.FirstResponseSeconds).HasColumnName("first_response_seconds");
        builder.Property(c => c.ClosedAt).HasColumnName("closed_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.Status, c.LastMessageAt }).HasDatabaseName("ix_conv_inbox");
        builder.HasIndex(c => new { c.TenantId, c.AssigneeId, c.Status });
        builder.HasIndex(c => c.ContactId);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Contact>().WithMany().HasForeignKey(c => c.ContactId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Domain.Channels.ChannelAccount>().WithMany().HasForeignKey(c => c.ChannelAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
