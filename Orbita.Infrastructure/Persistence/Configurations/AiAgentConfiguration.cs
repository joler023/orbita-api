using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class AiAgentConfiguration : IEntityTypeConfiguration<AiAgent>
{
    public void Configure(EntityTypeBuilder<AiAgent> builder)
    {
        builder.ToTable("ai_agents");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(a => a.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(a => a.SystemPrompt).HasColumnName("system_prompt").IsRequired();

        // Not in orbita-schema.dbml: what the owner actually typed, kept so the
        // configuration screen can show it back for editing. system_prompt above is what
        // gets sent to the model, composed from these two — see AiAgent.
        builder.Property(a => a.Personality)
            .HasColumnName("personality").HasMaxLength(AiAgent.PersonalityMaxLength).IsRequired();
        builder.Property(a => a.Instructions)
            .HasColumnName("instructions").HasMaxLength(AiAgent.InstructionsMaxLength).IsRequired();

        // Likewise: the named level the owner chose. temperature is derived from it.
        builder.Property(a => a.Tone)
            .HasColumnName("tone").HasConversion<string>().HasMaxLength(20).IsRequired();

        // Computed from Tone, never set independently — mapped so the column
        // orbita-schema.dbml specifies still holds a real value for anything reading the
        // table directly.
        builder.Property(a => a.Temperature)
            .HasColumnName("temperature").HasPrecision(3, 2).IsRequired();

        // jsonb, as the DBML specifies. A list of catalog keys, so it stays readable in
        // psql rather than becoming an opaque blob.
        builder.Property<List<string>>("_tools")
            .HasColumnName("tools")
            .HasColumnType("jsonb")
            .HasField("_tools")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();
        builder.Property(a => a.MaxTokens).HasColumnName("max_tokens").IsRequired();
        builder.Property(a => a.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        // tenant_id first, per the isolation rule in orbita-schema.dbml.
        builder.HasIndex(a => new { a.TenantId, a.IsEnabled }).HasDatabaseName("ix_ai_agents_tenant_enabled");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
