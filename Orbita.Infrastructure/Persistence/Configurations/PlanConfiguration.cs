using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Billing;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>Global reference data, not tenant-scoped — every tenant sees the same catalog.</summary>
public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(p => p.Code).HasColumnName("code").HasMaxLength(Plan.CodeMaxLength).IsRequired();
        builder.HasIndex(p => p.Code).IsUnique();

        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(Plan.NameMaxLength).IsRequired();
        builder.Property(p => p.IncludedConversations).HasColumnName("included_conversations").IsRequired();
        builder.Property(p => p.IncludedAiCredits).HasColumnName("included_ai_credits").IsRequired();
        builder.Property(p => p.PriceAmount).HasColumnName("price_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(p => p.PriceCurrency).HasColumnName("price_currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(p => p.StripePriceId).HasColumnName("stripe_price_id").HasMaxLength(200);
        builder.Property(p => p.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
