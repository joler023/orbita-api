using Orbita.Domain.Common;
using Orbita.Domain.Inbox;

namespace Orbita.Domain.Channels;

/// <summary>
/// A WhatsApp message template registered with Meta (ORB-B07) — the only way to start a
/// conversation, or reply after the 24h service window has closed. Alta manual en
/// Business Manager; this only mirrors Meta's own approval state locally
/// (<see cref="ApplyMetaStatus"/>), it never creates the template on Meta's side.
/// </summary>
public sealed class MessageTemplate : Entity
{
    public const int MetaTemplateNameMaxLength = 512;
    public const int LanguageMaxLength = 10;
    public const int RejectedReasonMaxLength = 500;
    private const string DefaultLanguage = "es";

    private MessageTemplate(
        Guid id,
        Guid tenantId,
        Guid channelAccountId,
        string metaTemplateName,
        MessageCategory category,
        string language,
        string body,
        TemplateStatus status,
        string? rejectedReason,
        DateTimeOffset? approvedAt,
        DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        ChannelAccountId = channelAccountId;
        MetaTemplateName = metaTemplateName;
        Category = category;
        Language = language;
        Body = body;
        Status = status;
        RejectedReason = rejectedReason;
        ApprovedAt = approvedAt;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; }

    public Guid ChannelAccountId { get; }

    public string MetaTemplateName { get; }

    public MessageCategory Category { get; }

    public string Language { get; }

    public string Body { get; }

    public TemplateStatus Status { get; private set; }

    public string? RejectedReason { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public bool IsApproved => Status == TemplateStatus.Approved;

    public static MessageTemplate Create(
        Guid tenantId, Guid channelAccountId, string metaTemplateName, MessageCategory category, string? language, string body, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new MessageTemplate(
            Guid.NewGuid(),
            tenantId,
            channelAccountId,
            RequireLength(metaTemplateName, MetaTemplateNameMaxLength, nameof(metaTemplateName)),
            category,
            string.IsNullOrWhiteSpace(language) ? DefaultLanguage : RequireLength(language, LanguageMaxLength, nameof(language)),
            RequireNonEmpty(body, nameof(body)),
            TemplateStatus.Draft,
            rejectedReason: null,
            approvedAt: null,
            now);
    }

    /// <summary>Reflects Meta's own review outcome for this template — never invented locally.</summary>
    public void ApplyMetaStatus(TemplateStatus status, string? reason, DateTimeOffset now)
    {
        Status = status;
        RejectedReason = status == TemplateStatus.Rejected ? reason : null;
        if (status == TemplateStatus.Approved)
        {
            ApprovedAt ??= now;
        }
    }

    /// <summary>Substitutes Meta's positional <c>{{1}}..{{n}}</c> placeholders.</summary>
    /// <exception cref="ArgumentException"><paramref name="variables"/>'s count doesn't match the template's own placeholder count.</exception>
    public string Render(IReadOnlyList<string> variables)
    {
        var placeholderCount = CountPlaceholders();
        if (variables.Count != placeholderCount)
        {
            throw new ArgumentException($"Template \"{MetaTemplateName}\" expects {placeholderCount} variable(s), got {variables.Count}.", nameof(variables));
        }

        var rendered = Body;
        for (var i = 0; i < variables.Count; i++)
        {
            rendered = rendered.Replace($"{{{{{i + 1}}}}}", variables[i], StringComparison.Ordinal);
        }

        return rendered;
    }

    private int CountPlaceholders()
    {
        var count = 0;
        while (Body.Contains($"{{{{{count + 1}}}}}", StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string RequireNonEmpty(string value, string paramName)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return trimmed;
    }

    private static string RequireLength(string value, int maxLength, string paramName)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{paramName} must be between 1 and {maxLength} characters.", paramName);
        }

        return trimmed;
    }
}
