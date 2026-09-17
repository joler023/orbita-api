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

        // ORB-C07. Not in orbita-schema.dbml, same class of addition as
        // last_message_preview.
        builder.Property(c => c.HandoffRequestedAt).HasColumnName("handoff_requested_at");
        builder.Property(c => c.HandoffReason).HasColumnName("handoff_reason").HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.HandoffSummary).HasColumnName("handoff_summary").HasMaxLength(Conversation.HandoffSummaryMaxLength);

        builder.HasIndex(c => new { c.TenantId, c.Status, c.LastMessageAt }).HasDatabaseName("ix_conv_inbox");
        builder.HasIndex(c => new { c.TenantId, c.AssigneeId, c.Status });
        builder.HasIndex(c => c.ContactId);

        // The handoff queue's own index: tenant first like every other one here, then the
        // wait start, which is both the filter (not null) and the order (oldest first).
        builder
            .HasIndex(c => new { c.TenantId, c.HandoffRequestedAt })
            .HasDatabaseName("ix_conv_handoff_queue")
            .HasFilter("handoff_requested_at IS NOT NULL");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Contact>().WithMany().HasForeignKey(c => c.ContactId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Domain.Channels.ChannelAccount>().WithMany().HasForeignKey(c => c.ChannelAccountId).OnDelete(DeleteBehavior.Restrict);

        // orbita-schema.dbml declares this one (`Ref: conversations.ai_agent_id >
        // ai_agents.id`) and it was simply missing, which mattered the moment ORB-C04
        // started writing the column: nothing stopped an assistant from being deleted out
        // from under the conversations it had answered, leaving a dangling id and a thread
        // that cannot say who replied. Restrict rather than SetNull for the same reason
        // ai_runs uses it — "who answered this" is history, not a nullable convenience.
        //
        // AiAgentService.DeleteAsync answers 409 before it ever gets here; this is the
        // backstop for any other path that assigns an agent, ORB-C08's router included.
        builder.HasOne<Domain.Ai.AiAgent>().WithMany().HasForeignKey(c => c.AiAgentId).OnDelete(DeleteBehavior.Restrict);
    }
}
