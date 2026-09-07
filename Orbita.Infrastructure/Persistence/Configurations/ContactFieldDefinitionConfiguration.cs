using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Crm;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class ContactFieldDefinitionConfiguration : IEntityTypeConfiguration<ContactFieldDefinition>
{
    public void Configure(EntityTypeBuilder<ContactFieldDefinition> builder)
    {
        builder.ToTable("contact_field_definitions");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(f => f.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(f => f.Key)
            .HasColumnName("key")
            .HasMaxLength(ContactFieldDefinition.KeyMaxLength)
            .IsRequired();
        builder.Property(f => f.Label)
            .HasColumnName("label")
            .HasMaxLength(ContactFieldDefinition.LabelMaxLength)
            .IsRequired();
        builder.Property(f => f.FieldType)
            .HasColumnName("field_type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(f => new { f.TenantId, f.Key })
            .IsUnique()
            .HasDatabaseName("ix_contact_field_definitions_tenant_key");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
