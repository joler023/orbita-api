using Orbita.Domain.Channels;
using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// One line of ORB-C08's router: "messages matching this go to that assistant, or to the
/// team". Rules are evaluated by <see cref="Position"/>, first match wins — and the order
/// is part of the data, not an implementation detail, because the story requires it to be
/// visible and configurable.
///
/// Not in orbita-schema.dbml, which has no routing table; it is an addition in the same
/// class as <c>tenant_model_preferences</c>. Tenant-scoped the normal way (RLS + query
/// filter + tenant-first index): rules are always read with the tenant already known.
///
/// Conditions are channel and keyword. The story also names <em>etiqueta</em>; there is no
/// tags table yet (orbita-schema.dbml's <c>tags</c> is unbuilt), so that condition does not
/// exist rather than existing and never matching.
/// </summary>
public sealed class RoutingRule : Entity
{
    public const int NameMaxLength = 120;

    public const int KeywordMaxLength = 120;

    public const int MaxRulesPerTenant = 50;

    private RoutingRule(Guid id, Guid tenantId, int position, string name, ChannelKind? channel, string? keyword, Guid? agentId, DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        Position = position;
        Name = name;
        Channel = channel;
        Keyword = keyword;
        AgentId = agentId;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; }

    public int Position { get; private set; }

    public string Name { get; private set; }

    /// <summary>Null matches any channel.</summary>
    public ChannelKind? Channel { get; private set; }

    /// <summary>Whole-word, accent-insensitive. Null matches any message.</summary>
    public string? Keyword { get; private set; }

    /// <summary>The assistant that takes the conversation. Null means "leave it for the team".</summary>
    public Guid? AgentId { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <exception cref="ArgumentException">Missing name, or a name or keyword too long.</exception>
    public static RoutingRule Create(Guid tenantId, int position, string name, ChannelKind? channel, string? keyword, Guid? agentId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Trim().Length > NameMaxLength)
        {
            throw new ArgumentException($"A rule name must be at most {NameMaxLength} characters.", nameof(name));
        }

        var trimmedKeyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();

        if (trimmedKeyword?.Length > KeywordMaxLength)
        {
            throw new ArgumentException($"A keyword must be at most {KeywordMaxLength} characters.", nameof(keyword));
        }

        return new RoutingRule(Guid.NewGuid(), tenantId, position, name.Trim(), channel, trimmedKeyword, agentId, now);
    }

    public bool Matches(ChannelKind channel, string? message)
        => (Channel is null || Channel == channel)
            && (Keyword is null || WholeWordText.Contains(message, Keyword));
}
