using Orbita.Domain.Channels;

namespace Orbita.Application.Channels;

/// <summary>Maps Meta's template review status strings to our own (ORB-B07) — PAUSED is folded into Disabled, we don't distinguish "paused" from "disabled" locally.</summary>
public static class MetaTemplateStatusMapper
{
    public static TemplateStatus Map(string metaStatus) => metaStatus.ToUpperInvariant() switch
    {
        "APPROVED" => TemplateStatus.Approved,
        "PENDING" or "IN_APPEAL" => TemplateStatus.Pending,
        "REJECTED" => TemplateStatus.Rejected,
        "PAUSED" or "DISABLED" => TemplateStatus.Disabled,
        _ => TemplateStatus.Pending,
    };
}
