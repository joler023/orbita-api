using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Billing;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>
/// Deliberately not RLS'd/query-filtered — see Subscription's class comment for why
/// (a webhook must be able to look this up by ProviderSubscriptionId before any
/// tenant is known). tenant_id is still indexed first, same convention as every
/// tenant-scoped table, since GetByTenantIdAsync is the hot lookup path.
/// </summary>
public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(s => s.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(s => s.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.ProviderCustomerId).HasColumnName("provider_customer_id").IsRequired();
        builder.Property(s => s.ProviderSubscriptionId).HasColumnName("provider_subscription_id");
        builder.Property(s => s.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.CurrentPeriodEnd).HasColumnName("current_period_end");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(s => s.TenantId).IsUnique();
        builder.HasIndex(s => new { s.Provider, s.ProviderSubscriptionId }).IsUnique();
    }
}
