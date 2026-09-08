using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;
using Orbita.Domain.Crm;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(c => c.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(Contact.DisplayNameMaxLength)
            .IsRequired();
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(Contact.PhoneMaxLength);
        builder.Property(c => c.InstagramUsername)
            .HasColumnName("instagram_username")
            .HasMaxLength(Contact.InstagramMaxLength);
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(Contact.EmailMaxLength);
        builder.Property(c => c.Channel)
            .HasColumnName("channel")
            .HasMaxLength(Contact.ChannelMaxLength)
            .IsRequired();
        builder.Property(c => c.CustomFields)
            .HasColumnName("custom_fields")
            .HasColumnType("jsonb")
            .HasConversion(
                fields => JsonSerializer.Serialize(fields, JsonOptions),
                json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>>(
                (left, right) => DictionaryEquals(left, right),
                dictionary => dictionary.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                dictionary => new Dictionary<string, string>(dictionary, StringComparer.OrdinalIgnoreCase)));
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.DisplayName }).HasDatabaseName("ix_contacts_tenant_name");
        builder.HasIndex(c => new { c.TenantId, c.Phone })
            .IsUnique()
            .HasFilter("phone IS NOT NULL")
            .HasDatabaseName("ix_contacts_tenant_phone");
        builder.HasIndex(c => new { c.TenantId, c.InstagramUsername })
            .IsUnique()
            .HasFilter("instagram_username IS NOT NULL")
            .HasDatabaseName("ix_contacts_tenant_instagram");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade);
    }

    private static bool DictionaryEquals(Dictionary<string, string>? left, Dictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || other != value)
            {
                return false;
            }
        }

        return true;
    }
}
