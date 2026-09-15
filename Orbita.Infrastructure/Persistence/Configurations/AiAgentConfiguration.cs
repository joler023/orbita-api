using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class AiAgentConfiguration : IEntityTypeConfiguration<AiAgent>
{
    private static readonly System.Text.Json.JsonSerializerOptions BusinessHoursJson = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public void Configure(EntityTypeBuilder<AiAgent> builder)
    {
        builder.ToTable("ai_agents");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(a => a.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(AiAgent.NameMaxLength).IsRequired();
        builder.Property(a => a.SystemPrompt).HasColumnName("system_prompt").IsRequired();

        // Not in orbita-schema.dbml: what the owner actually typed, kept so the
        // configuration screen can show it back for editing. system_prompt above is what
        // gets sent to the model, composed from these — see AiAgent.
        builder.Property(a => a.Personality)
            .HasColumnName("personality").HasMaxLength(AiAgent.PersonalityMaxLength).IsRequired();
        builder.Property(a => a.Instructions)
            .HasColumnName("instructions").HasMaxLength(AiAgent.InstructionsMaxLength).IsRequired();

        // The three sliders, each its own column. temperature and max_tokens are derived
        // from them.
        AgentStyleMapping.Configure(builder, a => a.Style);

        // Computed from the style, never set independently — mapped so the columns
        // orbita-schema.dbml specifies still hold real values for anything reading the
        // table directly.
        builder.Property(a => a.Temperature)
            .HasColumnName("temperature").HasPrecision(3, 2).IsRequired();
        builder.Property(a => a.MaxTokens).HasColumnName("max_tokens").IsRequired();

        // jsonb, as the DBML specifies. A list of catalog keys, so it stays readable in
        // psql rather than becoming an opaque blob.
        builder.Property<List<string>>("_tools")
            .HasColumnName("tools")
            .HasColumnType("jsonb")
            .HasField("_tools")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        // ORB-C06. jsonb like tools: a short list of the owner's own words, readable in psql.
        builder.Property<List<string>>("_blockedTopics")
            .HasColumnName("blocked_topics")
            .HasColumnType("jsonb")
            .HasField("_blockedTopics")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        builder.Property(a => a.OutOfScopeReply)
            .HasColumnName("out_of_scope_reply")
            .HasMaxLength(AiAgent.OutOfScopeReplyMaxLength)
            .IsRequired();

        // ORB-C08: orbita-schema.dbml's ai_agents.business_hours jsonb. Nullable = always on.
        // A value converter rather than an owned JSON type: BusinessHours is a positional
        // record, and EF binds owned types through constructors it cannot satisfy for one.
        // The column stays the jsonb the DBML specifies either way.
        builder.Property(a => a.BusinessHours)
            .HasColumnName("business_hours")
            .HasColumnType("jsonb")
            .HasConversion(
                hours => hours == null ? null : System.Text.Json.JsonSerializer.Serialize(hours, BusinessHoursJson),
                json => string.IsNullOrEmpty(json) ? null : System.Text.Json.JsonSerializer.Deserialize<BusinessHours>(json, BusinessHoursJson),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<BusinessHours?>(
                    (left, right) => System.Text.Json.JsonSerializer.Serialize(left, BusinessHoursJson) == System.Text.Json.JsonSerializer.Serialize(right, BusinessHoursJson),
                    hours => System.Text.Json.JsonSerializer.Serialize(hours, BusinessHoursJson).GetHashCode(),
                    hours => hours == null ? null : System.Text.Json.JsonSerializer.Deserialize<BusinessHours>(System.Text.Json.JsonSerializer.Serialize(hours, BusinessHoursJson), BusinessHoursJson)));

        builder.Property(a => a.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        // tenant_id first, per the isolation rule in orbita-schema.dbml.
        builder.HasIndex(a => new { a.TenantId, a.IsEnabled }).HasDatabaseName("ix_ai_agents_tenant_enabled");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
