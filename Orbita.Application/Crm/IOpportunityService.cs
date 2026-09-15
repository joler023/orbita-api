namespace Orbita.Application.Crm;

public interface IOpportunityService
{
    Task<PipelineBoard> GetBoardAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid pipelineId,
        OpportunityBoardQuery query,
        CancellationToken cancellationToken);

    Task<OpportunitySummary> CreateAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid pipelineId,
        CreateOpportunityRequest request,
        CancellationToken cancellationToken);

    Task<OpportunitySummary> UpdateAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid opportunityId,
        UpdateOpportunityRequest request,
        CancellationToken cancellationToken);

    Task<OpportunitySummary> MoveAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid opportunityId,
        MoveOpportunityRequest request,
        CancellationToken cancellationToken);
}
