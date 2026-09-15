namespace Orbita.Application.Crm;

public interface IPipelineService
{
    Task<IReadOnlyList<PipelineSummary>> ListAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken);

    Task<PipelineSummary> GetAsync(Guid tenantId, Guid callerUserId, Guid pipelineId, CancellationToken cancellationToken);

    Task<PipelineSummary> CreateAsync(Guid tenantId, Guid callerUserId, CreatePipelineRequest request, CancellationToken cancellationToken);

    Task<PipelineSummary> UpdateAsync(Guid tenantId, Guid callerUserId, Guid pipelineId, UpdatePipelineRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid tenantId, Guid callerUserId, Guid pipelineId, CancellationToken cancellationToken);

    Task<PipelineSummary> CreateStageAsync(Guid tenantId, Guid callerUserId, Guid pipelineId, CreateStageRequest request, CancellationToken cancellationToken);

    Task<PipelineSummary> UpdateStageAsync(Guid tenantId, Guid callerUserId, Guid pipelineId, Guid stageId, UpdateStageRequest request, CancellationToken cancellationToken);

    Task<PipelineSummary> ReorderStagesAsync(Guid tenantId, Guid callerUserId, Guid pipelineId, ReorderStagesRequest request, CancellationToken cancellationToken);

    Task<PipelineSummary> DeleteStageAsync(Guid tenantId, Guid callerUserId, Guid pipelineId, Guid stageId, DeleteStageRequest request, CancellationToken cancellationToken);
}
