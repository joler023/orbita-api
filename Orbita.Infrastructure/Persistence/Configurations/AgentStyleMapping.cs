using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="AgentStyle"/> onto three columns, shared by <c>ai_agents</c> and
/// <c>ai_agent_drafts</c>.
///
/// Three columns rather than one jsonb blob, because each axis is something you would
/// plausibly filter or report on ("how many assistants are set to Brief?"), and because a
/// blob would hide a typo in an axis name until runtime.
///
/// Extracted because the two tables must map it identically — a draft that stored its
/// style differently from the agent it publishes onto would round-trip wrong in ways no
/// test would obviously catch.
/// </summary>
internal static class AgentStyleMapping
{
    /// <param name="style">
    /// Declared nullable only because that is the signature <c>ComplexProperty</c> takes;
    /// the property itself never is, and the mapping below makes every column required.
    /// </param>
    public static void Configure<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, AgentStyle?>> style)
        where TEntity : class
    {
        builder.ComplexProperty(style, complex =>
        {
            complex.Property(s => s.Formality)
                .HasColumnName("formality").HasConversion<string>().HasMaxLength(20).IsRequired();

            complex.Property(s => s.Verbosity)
                .HasColumnName("verbosity").HasConversion<string>().HasMaxLength(20).IsRequired();

            complex.Property(s => s.Energy)
                .HasColumnName("energy").HasConversion<string>().HasMaxLength(20).IsRequired();
        });
    }
}
