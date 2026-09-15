namespace Orbita.Domain.Crm;

public interface IPipelineStageRepository
{
    Task<PipelineStage?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PipelineStage>> GetByPipelineAsync(Guid pipelineId, CancellationToken cancellationToken);

    Task<int> CountByPipelineAsync(Guid pipelineId, CancellationToken cancellationToken);

    Task AddAsync(PipelineStage stage, CancellationToken cancellationToken);

    void Remove(PipelineStage stage);
}
