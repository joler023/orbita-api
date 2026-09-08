using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Channels;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>
/// Deliberately not RLS'd/query-filtered — see ChannelAccount's class comment: an
/// inbound Meta webhook must resolve the tenant from (kind, external_id) before any
/// tenant is known, which orbita-schema.dbml's ux_channel_external index exists for.
/// The (tenant_id, kind) index keeps the tenant-first convention for the dashboard's
/// own listing path.
/// </summary>
public sealed class ChannelAccountConfiguration : IEntityTypeConfiguration<ChannelAccount>
{
    public void Configure(EntityTypeBuilder<ChannelAccount> builder)
    {
        builder.ToTable("channel_accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(a => a.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(a => a.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.ExternalId).HasColumnName("external_id").HasMaxLength(ChannelAccount.ExternalIdMaxLength).IsRequired();
        builder.Property(a => a.WabaId).HasColumnName("waba_id").HasMaxLength(ChannelAccount.WabaIdMaxLength);
        builder.Property(a => a.DisplayName).HasColumnName("display_name").HasMaxLength(ChannelAccount.DisplayNameMaxLength).IsRequired();
        builder.Property(a => a.PhoneE164).HasColumnName("phone_e164").HasMaxLength(ChannelAccount.PhoneMaxLength);
        builder.Property(a => a.CredentialsRef).HasColumnName("credentials_ref").IsRequired();
        builder.Property(a => a.WebhookSecret).HasColumnName("webhook_secret").IsRequired();
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.TokenExpiresAt).HasColumnName("token_expires_at");
        builder.Property(a => a.ConnectedAt).HasColumnName("connected_at");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(a => new { a.Kind, a.ExternalId })
            .IsUnique()
            .HasDatabaseName("ux_channel_external");
        builder.HasIndex(a => new { a.TenantId, a.Kind })
            .HasDatabaseName("ix_channel_accounts_tenant_kind");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
