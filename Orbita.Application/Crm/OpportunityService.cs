using Orbita.Application.Audit;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;

namespace Orbita.Application.Crm;

public sealed class OpportunityService(
    IPipelineRepository pipelineRepository,
    IPipelineStageRepository stageRepository,
    IOpportunityRepository opportunityRepository,
    IMembershipRepository membershipRepository,
    IUserRepository userRepository,
    ITenantAuthorizationService authorizationService,
    ICrmRealtimePublisher realtimePublisher,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOpportunityService
{
    public async Task<PipelineBoard> GetBoardAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid pipelineId,
        OpportunityBoardQuery query,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ViewPipeline, cancellationToken);
        var pipeline = await RequirePipelineAsync(tenantId, pipelineId, cancellationToken);
        var stages = await unitOfWork.QueryInTenantScopeAsync(
            ct => stageRepository.GetByPipelineAsync(pipelineId, ct),
            cancellationToken);
        var opportunities = await unitOfWork.QueryInTenantScopeAsync(
            ct => opportunityRepository.GetByPipelineAsync(
                pipelineId,
                query.AssignedToUserId,
                query.CreatedFrom,
                query.CreatedTo,
                ct),
            cancellationToken);

        var names = await LoadAssigneeNamesAsync(opportunities, cancellationToken);
        var byStage = opportunities.GroupBy(o => o.StageId).ToDictionary(g => g.Key, g => g.ToArray());

        var columns = stages
            .OrderBy(s => s.SortOrder)
            .Select(stage =>
            {
                var cards = byStage.GetValueOrDefault(stage.Id, []);
                return new BoardStageSummary(
                    stage.Id,
                    stage.Name,
                    stage.SortOrder,
                    stage.IsWon,
                    stage.IsLost,
                    cards.Sum(c => c.Amount ?? 0m),
                    cards
                        .OrderByDescending(c => c.UpdatedAt)
                        .Select(c => ToSummary(c, names))
                        .ToArray());
            })
            .ToArray();

        return new PipelineBoard(pipeline.Id, pipeline.Name, columns);
    }

    public async Task<OpportunitySummary> CreateAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid pipelineId,
        CreateOpportunityRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageOpportunities, cancellationToken);
        var pipeline = await RequirePipelineAsync(tenantId, pipelineId, cancellationToken);
        var stages = await unitOfWork.QueryInTenantScopeAsync(
            ct => stageRepository.GetByPipelineAsync(pipelineId, ct),
            cancellationToken);
        var stage = request.StageId is { } stageId
            ? stages.FirstOrDefault(s => s.Id == stageId) ?? throw new StageNotFoundException()
            : stages.OrderBy(s => s.SortOrder).FirstOrDefault() ?? throw new StageNotFoundException();

        await EnsureAssigneeAsync(tenantId, request.AssignedToUserId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var opportunity = Opportunity.Create(
            tenantId,
            pipeline.Id,
            stage.Id,
            request.Title,
            request.Amount,
            now,
            request.AssignedToUserId);

        await opportunityRepository.AddAsync(opportunity, cancellationToken);
        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "opportunity.created",
            nameof(Opportunity),
            opportunity.Id,
            new { opportunity.Title, opportunity.StageId },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var summary = await ToSummaryAsync(opportunity, cancellationToken);
        await realtimePublisher.PublishOpportunityChangedAsync(
            tenantId,
            new OpportunityChangedEvent(OpportunityChangedKind.Created, Guid.NewGuid(), summary),
            cancellationToken);
        return summary;
    }

    public async Task<OpportunitySummary> UpdateAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid opportunityId,
        UpdateOpportunityRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageOpportunities, cancellationToken);
        var opportunity = await RequireOpportunityAsync(tenantId, opportunityId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (request.Title is not null)
        {
            opportunity.Rename(request.Title, now);
        }

        if (request.Amount is not null)
        {
            opportunity.SetAmount(request.Amount, now);
        }

        if (request.ClearAssignee)
        {
            opportunity.AssignTo(null, now);
        }
        else if (request.AssignedToUserId is not null)
        {
            await EnsureAssigneeAsync(tenantId, request.AssignedToUserId, cancellationToken);
            opportunity.AssignTo(request.AssignedToUserId, now);
        }

        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "opportunity.updated",
            nameof(Opportunity),
            opportunity.Id,
            new { opportunity.Title, opportunity.Amount, opportunity.AssignedToUserId },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var summary = await ToSummaryAsync(opportunity, cancellationToken);
        await realtimePublisher.PublishOpportunityChangedAsync(
            tenantId,
            new OpportunityChangedEvent(OpportunityChangedKind.Updated, Guid.NewGuid(), summary),
            cancellationToken);
        return summary;
    }

    public async Task<OpportunitySummary> MoveAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid opportunityId,
        MoveOpportunityRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageOpportunities, cancellationToken);
        var opportunity = await RequireOpportunityAsync(tenantId, opportunityId, cancellationToken);
        var destination = await unitOfWork.QueryInTenantScopeAsync(
            ct => stageRepository.GetByIdAsync(request.StageId, ct),
            cancellationToken);

        if (destination is null || destination.PipelineId != opportunity.PipelineId)
        {
            throw new StageNotFoundException();
        }

        var applied = opportunity.MoveToStage(destination.Id, request.EventId, timeProvider.GetUtcNow());
        var summary = await ToSummaryAsync(opportunity, cancellationToken);

        if (!applied)
        {
            return summary;
        }

        await auditLogger.RecordAsync(
            tenantId,
            callerUserId,
            "opportunity.moved",
            nameof(Opportunity),
            opportunity.Id,
            new { stageId = destination.Id, eventId = request.EventId },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await realtimePublisher.PublishOpportunityChangedAsync(
            tenantId,
            new OpportunityChangedEvent(OpportunityChangedKind.Moved, request.EventId, summary),
            cancellationToken);
        return summary;
    }

    private async Task EnsureAssigneeAsync(Guid tenantId, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return;
        }

        var membership = await unitOfWork.QueryInTenantScopeAsync(
            ct => membershipRepository.GetByTenantAndUserAsync(tenantId, userId.Value, ct),
            cancellationToken);

        if (membership is null || !membership.IsActive || membership.IsPending)
        {
            throw new AssigneeNotInTenantException();
        }
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

    private async Task<Opportunity> RequireOpportunityAsync(Guid tenantId, Guid opportunityId, CancellationToken cancellationToken)
    {
        var opportunity = await unitOfWork.QueryInTenantScopeAsync(
            ct => opportunityRepository.GetByIdAsync(opportunityId, ct),
            cancellationToken);

        if (opportunity is null || opportunity.TenantId != tenantId)
        {
            throw new OpportunityNotFoundException();
        }

        return opportunity;
    }

    private async Task<OpportunitySummary> ToSummaryAsync(Opportunity opportunity, CancellationToken cancellationToken)
    {
        var names = await LoadAssigneeNamesAsync([opportunity], cancellationToken);
        return ToSummary(opportunity, names);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadAssigneeNamesAsync(
        IReadOnlyList<Opportunity> opportunities,
        CancellationToken cancellationToken)
    {
        var ids = opportunities
            .Select(o => o.AssignedToUserId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var users = await userRepository.GetByIdsAsync(ids, cancellationToken);
        return users.ToDictionary(u => u.Id, u => u.FullName);
    }

    private static OpportunitySummary ToSummary(Opportunity opportunity, IReadOnlyDictionary<Guid, string> names)
        => new(
            opportunity.Id,
            opportunity.PipelineId,
            opportunity.StageId,
            opportunity.Title,
            opportunity.Amount,
            opportunity.AssignedToUserId,
            opportunity.AssignedToUserId is { } assigneeId && names.TryGetValue(assigneeId, out var name)
                ? name
                : null,
            opportunity.LastMoveEventId,
            opportunity.CreatedAt);
}
