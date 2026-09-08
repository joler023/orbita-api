namespace Orbita.Application.Crm;

public sealed record OpportunityChangedEvent(
    OpportunityChangedKind Kind,
    Guid EventId,
    OpportunitySummary Opportunity);
