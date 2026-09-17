using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>
/// ORB-C11's <c>agent_test_cases</c>. Not in orbita-schema.dbml, same class of addition as
/// <c>routing_rules</c>: the criterion asks for saved cases and the DBML has nowhere to put
/// them.
/// </summary>
public sealed class AgentTestCaseConfiguration : IEntityTypeConfiguration<AgentTestCase>
{
    private static readonly JsonSerializerOptions TurnsJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public void Configure(EntityTypeBuilder<AgentTestCase> builder)
    {
        builder.ToTable("agent_test_cases");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(c => c.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(AgentTestCase.NameMaxLength).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();

        // Same value-converter route as ai_agents.business_hours, and for the same reason:
        // AgentTestTurn is a positional record, which EF cannot bind as an owned type.
        builder.Property(c => c.Turns)
            .HasColumnName("turns")
            .HasColumnType("jsonb")
            .HasConversion(
                turns => JsonSerializer.Serialize(turns, TurnsJson),
                json => JsonSerializer.Deserialize<List<AgentTestTurn>>(json, TurnsJson)!,
                new ValueComparer<IReadOnlyList<AgentTestTurn>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    turns => turns.Aggregate(0, (hash, turn) => HashCode.Combine(hash, turn.GetHashCode())),
                    turns => turns.ToList()))
            .IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.AgentId }).HasDatabaseName("ix_agent_test_cases_tenant_agent");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade);

        // Cascade, unlike ai_runs' RESTRICT: a saved test case is the owner's scratch work,
        // not a consumption record, so it must never be the reason a deletable assistant
        // cannot be deleted.
        builder.HasOne<AiAgent>().WithMany().HasForeignKey(c => c.AgentId).OnDelete(DeleteBehavior.Cascade);
    }
}
