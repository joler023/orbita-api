namespace Orbita.Domain.Crm;

public interface IOpportunityRepository
{
    Task<int> CountByStageAsync(Guid stageId, CancellationToken cancellationToken);

    Task<int> CountByPipelineAsync(Guid pipelineId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Opportunity>> GetByStageAsync(Guid stageId, CancellationToken cancellationToken);

    Task AddAsync(Opportunity opportunity, CancellationToken cancellationToken);
}
