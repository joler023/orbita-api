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

    /// <summary>
    /// ORB-C05's <c>crear_oportunidad</c>: an opportunity the assistant opens for the
    /// customer it is talking to. Always on the tenant's default pipeline, first stage, and
    /// always for that conversation's contact — none of those are the model's to choose, so
    /// none of them can be pointed at another tenant's data by a model that invents an id.
    ///
    /// No permission check, same reasoning as the other agent entry points: the actor is
    /// not a person. What stands in for authorization is that the tool is enabled on the
    /// agent. Audited with <c>actor_type = ai_agent</c>, which is an explicit criterion of
    /// the story.
    /// </summary>
    /// <exception cref="PipelineNotFoundException">The tenant has no default pipeline.</exception>
    Task<OpportunitySummary> CreateForAgentAsync(
        Guid tenantId,
        Guid agentId,
        Guid contactId,
        string title,
        decimal? amount,
        CancellationToken cancellationToken);

    /// <summary>
    /// ORB-C05's <c>mover_etapa</c>: moves the conversation contact's most recent open
    /// opportunity to the stage with this name. Takes a stage <em>name</em> and no
    /// opportunity id on purpose: the model knows the words on the board, not the ids, and
    /// an id it supplied would be the one path to another customer's — or another
    /// tenant's — record.
    /// </summary>
    /// <returns>Null when the contact has no open opportunity to move.</returns>
    /// <exception cref="StageNotFoundException">No stage with that name on the opportunity's pipeline.</exception>
    Task<OpportunitySummary?> MoveContactOpportunityForAgentAsync(
        Guid tenantId,
        Guid agentId,
        Guid contactId,
        string stageName,
        CancellationToken cancellationToken);
}
