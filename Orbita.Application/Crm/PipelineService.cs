using Orbita.Application.Audit;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;

namespace Orbita.Application.Crm;

public sealed class PipelineService(
    IPipelineRepository pipelineRepository,
    IPipelineStageRepository stageRepository,
    IOpportunityRepository opportunityRepository,
    ITenantAuthorizationService authorizationService,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IPipelineService
{
    public async Task<IReadOnlyList<PipelineSummary>> ListAsync(Guid tenantId, Guid callerUserId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewPipeline, cancellationToken);
        var pipelines = await unitOfWork.QueryInTenantScopeAsync(
            ct => pipelineRepository.GetByTenantAsync(tenantId, ct),
            cancellationToken);

        var summaries = new List<PipelineSummary>(pipelines.Count);
        foreach (var pipeline in pipelines)
        {
            summaries.Add(await ToSummaryAsync(pipeline, cancellationToken));
        }

        return summaries;
    }

    public async Task<PipelineSummary> GetAsync(Guid tenantId, Guid callerUserId, Guid pipelineId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewPipeline, cancellationToken);
        var pipeline = await RequirePipelineAsync(tenantId, pipelineId, cancellationToken);
        return await ToSummaryAsync(pipeline, cancellationToken);
    }

    public async Task<PipelineSummary> CreateAsync(
        Guid tenantId,
        Guid callerUserId,
        CreatePipelineRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManagePipeline, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var pipeline = Pipeline.Create(tenantId, request.Name, isDefault: false, now);
        var firstStage = PipelineStage.Create(tenantId, pipeline.Id, "Nuevo", 0, isWon: false, isLost: false, now);

        await pipelineRepository.AddAsync(pipeline, cancellationToken);
        await stageRepository.AddAsync(firstStage, cancellationToken);
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "pipeline.created",
            nameof(Pipeline),
            pipeline.Id,
            new { pipeline.Name },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ToSummaryAsync(pipeline, cancellationToken);
    }

    public async Task<PipelineSummary> UpdateAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid pipelineId,
        UpdatePipelineRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManagePipeline, cancellationToken);
        var pipeline = await RequirePipelineAsync(tenantId, pipelineId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (request.Name is not null)
        {
            pipeline.Rename(request.Name, now);
        }

        if (request.IsDefault == true && !pipeline.IsDefault)
        {
            await MakeDefaultAsync(tenantId, pipeline, now, cancellationToken);
        }

        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "pipeline.updated",
            nameof(Pipeline),
            pipeline.Id,
            new { pipeline.Name, pipeline.IsDefault },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ToSummaryAsync(pipeline, cancellationToken);
    }

    public async Task DeleteAsync(Guid tenantId, Guid callerUserId, Guid pipelineId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManagePipeline, cancellationToken);
        var pipeline = await RequirePipelineAsync(tenantId, pipelineId, cancellationToken);

        var pipelineCount = await unitOfWork.QueryInTenantScopeAsync(
            ct => pipelineRepository.CountByTenantAsync(tenantId, ct),
            cancellationToken);
        if (pipelineCount <= 1)
        {
            throw new CannotDeleteLastPipelineException();
        }

        var opportunityCount = await unitOfWork.QueryInTenantScopeAsync(
            ct => opportunityRepository.CountByPipelineAsync(pipelineId, ct),
            cancellationToken);
        if (opportunityCount > 0)
        {
            throw new PipelineHasOpportunitiesException();
        }

        var stages = await unitOfWork.QueryInTenantScopeAsync(
            ct => stageRepository.GetByPipelineAsync(pipelineId, ct),
            cancellationToken);
        foreach (var stage in stages)
        {
            stageRepository.Remove(stage);
        }

        var now = timeProvider.GetUtcNow();
        if (pipeline.IsDefault)
        {
            var remaining = await unitOfWork.QueryInTenantScopeAsync(
                ct => pipelineRepository.GetByTenantAsync(tenantId, ct),
                cancellationToken);
            var successor = remaining.First(p => p.Id != pipeline.Id);
            successor.MarkDefault(now);
        }

        pipelineRepository.Remove(pipeline);
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "pipeline.deleted",
            nameof(Pipeline),
            pipeline.Id,
            new { pipeline.Name },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PipelineSummary> CreateStageAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid pipelineId,
        CreateStageRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManagePipeline, cancellationToken);
        var pipeline = await RequirePipelineAsync(tenantId, pipelineId, cancellationToken);
        var existing = await unitOfWork.QueryInTenantScopeAsync(
            ct => stageRepository.GetByPipelineAsync(pipelineId, ct),
            cancellationToken);

        var now = timeProvider.GetUtcNow();
        var stage = PipelineStage.Create(
            tenantId,
            pipeline.Id,
            request.Name,
            existing.Count,
            request.IsWon,
            request.IsLost,
            now);

        await stageRepository.AddAsync(stage, cancellationToken);
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "pipeline_stage.created",
            nameof(PipelineStage),
            stage.Id,
            new { pipelineId, stage.Name },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ToSummaryAsync(pipeline, cancellationToken);
    }

    public async Task<PipelineSummary> UpdateStageAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid pipelineId,
        Guid stageId,
        UpdateStageRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManagePipeline, cancellationToken);
        var pipeline = await RequirePipelineAsync(tenantId, pipelineId, cancellationToken);
        var stage = await RequireStageAsync(pipelineId, stageId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (request.Name is not null)
        {
            stage.Rename(request.Name, now);
        }

        if (request.IsWon is not null || request.IsLost is not null)
        {
            stage.SetOutcome(request.IsWon ?? stage.IsWon, request.IsLost ?? stage.IsLost, now);
        }

        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "pipeline_stage.updated",
            nameof(PipelineStage),
            stage.Id,
            new { stage.Name, stage.IsWon, stage.IsLost },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ToSummaryAsync(pipeline, cancellationToken);
    }

    public async Task<PipelineSummary> ReorderStagesAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid pipelineId,
        ReorderStagesRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManagePipeline, cancellationToken);
        var pipeline = await RequirePipelineAsync(tenantId, pipelineId, cancellationToken);
        var stages = await unitOfWork.QueryInTenantScopeAsync(
            ct => stageRepository.GetByPipelineAsync(pipelineId, ct),
            cancellationToken);

        if (request.StageIds.Count != stages.Count || request.StageIds.Distinct().Count() != stages.Count)
        {
            throw new ArgumentException("Stage order must list every stage of the pipeline exactly once.");
        }

        var byId = stages.ToDictionary(s => s.Id);
        var now = timeProvider.GetUtcNow();
        for (var index = 0; index < request.StageIds.Count; index++)
        {
            if (!byId.TryGetValue(request.StageIds[index], out var stage))
            {
                throw new StageNotFoundException();
            }

            stage.SetSortOrder(index, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToSummaryAsync(pipeline, cancellationToken);
    }

    public async Task<PipelineSummary> DeleteStageAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid pipelineId,
        Guid stageId,
        DeleteStageRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManagePipeline, cancellationToken);
        var pipeline = await RequirePipelineAsync(tenantId, pipelineId, cancellationToken);
        var stage = await RequireStageAsync(pipelineId, stageId, cancellationToken);

        var stageCount = await unitOfWork.QueryInTenantScopeAsync(
            ct => stageRepository.CountByPipelineAsync(pipelineId, ct),
            cancellationToken);
        if (stageCount <= 1)
        {
            throw new CannotDeleteLastStageException();
        }

        var opportunityCount = await unitOfWork.QueryInTenantScopeAsync(
            ct => opportunityRepository.CountByStageAsync(stageId, ct),
            cancellationToken);
        if (opportunityCount > 0)
        {
            if (request.RelocateToStageId is null)
            {
                throw new StageHasOpportunitiesException();
            }

            var destination = await RequireStageAsync(pipelineId, request.RelocateToStageId.Value, cancellationToken);
            if (destination.Id == stage.Id)
            {
                throw new InvalidStageRelocateException();
            }

            var now = timeProvider.GetUtcNow();
            var opportunities = await unitOfWork.QueryInTenantScopeAsync(
                ct => opportunityRepository.GetByStageAsync(stageId, ct),
                cancellationToken);
            foreach (var opportunity in opportunities)
            {
                opportunity.MoveToStage(destination.Id, Guid.NewGuid(), now);
            }
        }

        stageRepository.Remove(stage);
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "pipeline_stage.deleted",
            nameof(PipelineStage),
            stage.Id,
            new { pipelineId, relocateToStageId = request.RelocateToStageId },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ToSummaryAsync(pipeline, cancellationToken);
    }

    private async Task MakeDefaultAsync(Guid tenantId, Pipeline pipeline, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pipelines = await unitOfWork.QueryInTenantScopeAsync(
            ct => pipelineRepository.GetByTenantAsync(tenantId, ct),
            cancellationToken);

        foreach (var other in pipelines.Where(p => p.IsDefault && p.Id != pipeline.Id))
        {
            other.ClearDefault(now);
        }

        pipeline.MarkDefault(now);
    }

    private async Task<Pipeline> RequirePipelineAsync(Guid tenantId, Guid pipelineId, CancellationToken cancellationToken)
    {
        var pipeline = await unitOfWork.QueryInTenantScopeAsync(
            ct => pipelineRepository.GetByIdAsync(pipelineId, ct),
            cancellationToken);

        if (pipeline is null || pipeline.TenantId != tenantId)
        {
            throw new PipelineNotFoundException();
        }

        return pipeline;
    }

    private async Task<PipelineStage> RequireStageAsync(Guid pipelineId, Guid stageId, CancellationToken cancellationToken)
    {
        var stage = await unitOfWork.QueryInTenantScopeAsync(
            ct => stageRepository.GetByIdAsync(stageId, ct),
            cancellationToken);

        if (stage is null || stage.PipelineId != pipelineId)
        {
            throw new StageNotFoundException();
        }

        return stage;
    }

    private async Task<PipelineSummary> ToSummaryAsync(Pipeline pipeline, CancellationToken cancellationToken)
    {
        var stages = await unitOfWork.QueryInTenantScopeAsync(
            ct => stageRepository.GetByPipelineAsync(pipeline.Id, ct),
            cancellationToken);

        return new PipelineSummary(
            pipeline.Id,
            pipeline.Name,
            pipeline.IsDefault,
            stages
                .OrderBy(s => s.SortOrder)
                .Select(s => new StageSummary(s.Id, s.Name, s.SortOrder, s.IsWon, s.IsLost))
                .ToArray());
    }
}
