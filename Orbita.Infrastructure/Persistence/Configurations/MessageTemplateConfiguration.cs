using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Channels;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("message_templates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(t => t.ChannelAccountId).HasColumnName("channel_account_id").IsRequired();
        builder.Property(t => t.MetaTemplateName).HasColumnName("meta_template_name").HasMaxLength(MessageTemplate.MetaTemplateNameMaxLength).IsRequired();
        builder.Property(t => t.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Language).HasColumnName("language").HasMaxLength(MessageTemplate.LanguageMaxLength).IsRequired();
        builder.Property(t => t.Body).HasColumnName("body").IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.RejectedReason).HasColumnName("rejected_reason").HasMaxLength(MessageTemplate.RejectedReasonMaxLength);
        builder.Property(t => t.ApprovedAt).HasColumnName("approved_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(t => new { t.ChannelAccountId, t.MetaTemplateName, t.Language }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.Status });

        builder.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
