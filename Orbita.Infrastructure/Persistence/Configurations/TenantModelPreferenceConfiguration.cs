using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class TenantModelPreferenceConfiguration : IEntityTypeConfiguration<TenantModelPreference>
{
    public void Configure(EntityTypeBuilder<TenantModelPreference> builder)
    {
        builder.ToTable("tenant_model_preferences");

        builder.HasKey(preference => preference.Id);
        builder.Property(preference => preference.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(preference => preference.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(preference => preference.ProviderName)
            .HasColumnName("provider_name")
            .HasMaxLength(TenantModelPreference.ProviderNameMaxLength)
            .IsRequired();

        builder.Property(preference => preference.Task)
            .HasColumnName("task")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(preference => preference.Model)
            .HasColumnName("model")
            .HasMaxLength(TenantModelPreference.ModelMaxLength)
            .IsRequired();

        builder.Property(preference => preference.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // tenant_id first, per the isolation rule. Unique so a tenant cannot end up with
        // two answers for the same question — which one won would be arbitrary.
        builder.HasIndex(preference => new { preference.TenantId, preference.ProviderName, preference.Task })
            .IsUnique()
            .HasDatabaseName("ux_tenant_model_preferences_tenant_provider_task");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(preference => preference.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
