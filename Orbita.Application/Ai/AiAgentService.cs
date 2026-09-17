using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Ai;

public sealed class AiAgentService(
    IAiAgentRepository agentRepository,
    IAiAgentDraftRepository draftRepository,
    IAiRunRepository runRepository,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAiAgentService
{
    public async Task<IReadOnlyList<AiAgentDto>> ListAsync(
        Guid tenantId,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        // Both in one tenant-scoped transaction: the list screen shows the "unpublished
        // changes" badge per row, and a query per row would be N+1 for a badge.
        var (agents, drafts) = await unitOfWork.QueryInTenantScopeAsync(
            async ct => (
                await agentRepository.ListByTenantAsync(tenantId, ct),
                await draftRepository.ListByTenantAsync(tenantId, ct)),
            cancellationToken);

        var draftsByAgent = drafts.ToDictionary(draft => draft.AgentId);

        return agents
            .Select(agent => AiAgentDto.From(agent, draftsByAgent.GetValueOrDefault(agent.Id)))
            .ToList();
    }

    public async Task<AiAgentDto> GetAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var (agent, draft) = await LoadAsync(tenantId, agentId, cancellationToken);

        return AiAgentDto.From(agent, draft);
    }

    public async Task<AiAgentDto> CreateAsync(
        Guid tenantId,
        Guid callerUserId,
        SaveAiAgentRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);

        var agent = AiAgent.Create(
            tenantId,
            request.Name,
            request.Personality,
            request.Instructions,
            request.Style.ToStyle(),
            timeProvider.GetUtcNow());

        agent.EnableTools(request.Tools);

        agentRepository.Add(agent);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AiAgentDto.From(agent);
    }

    public async Task<AiAgentDto> SaveDraftAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        SaveAiAgentRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);

        var (agent, existing) = await LoadAsync(tenantId, agentId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var style = request.Style.ToStyle();

        // Validated against the same rules the live agent enforces, so a draft can never
        // hold something that would fail on publish — the owner finds out now, not after
        // clicking Publish.
        AiAgent.Create(tenantId, request.Name, request.Personality, request.Instructions, style, now)
            .EnableTools(request.Tools);

        AiAgentDraft draft;

        if (existing is null)
        {
            draft = AiAgentDraft.For(agent, request.Name, request.Personality, request.Instructions, style, request.Tools, now);
            draftRepository.Add(draft);
        }
        else
        {
            existing.Replace(request.Name, request.Personality, request.Instructions, style, request.Tools, now);
            draft = existing;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AiAgentDto.From(agent, draft);
    }

    public async Task<AiAgentDto> PublishAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var (agent, draft) = await LoadAsync(tenantId, agentId, cancellationToken);

        if (draft is null || !draft.DiffersFrom(agent))
        {
            throw new NothingToPublishException();
        }

        agent.ApplyConfiguration(draft.Name, draft.Personality, draft.Instructions, draft.Style, draft.Tools);

        // The draft goes in the same transaction as the change it describes: a published
        // draft that survived would keep the screen claiming there are pending changes.
        draftRepository.Remove(draft);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AiAgentDto.From(agent);
    }

    public async Task<AiAgentDto> DiscardDraftAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var (agent, draft) = await LoadAsync(tenantId, agentId, cancellationToken);

        // Discarding nothing is the desired end state, not an error.
        if (draft is not null)
        {
            draftRepository.Remove(draft);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return AiAgentDto.From(agent);
    }

    public async Task<AiAgentDto> SetGuardrailsAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        IReadOnlyList<string> blockedTopics,
        string outOfScopeReply,
        string? handoffReply,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var (agent, draft) = await LoadAsync(tenantId, agentId, cancellationToken);

        // Straight onto the live agent, draft untouched — see AgentGuardrailsDto. A
        // missing handoffReply keeps the stored one: the field arrived after this endpoint
        // had a consumer, and a body written before it exists must still save.
        agent.SetGuardrails(blockedTopics, outOfScopeReply, handoffReply ?? agent.HandoffReply);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AiAgentDto.From(agent, draft);
    }

    public async Task<AiAgentDto> SetEnabledAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var (agent, draft) = await LoadAsync(tenantId, agentId, cancellationToken);

        // A pending draft survives the switch untouched: turning an assistant on publishes
        // nothing, and turning it off discards nothing.
        agent.SetEnabled(isEnabled);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AiAgentDto.From(agent, draft);
    }

    public async Task DeleteAsync(Guid tenantId, Guid callerUserId, Guid agentId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var agents = await unitOfWork.QueryInTenantScopeAsync(
            ct => agentRepository.ListByTenantAsync(tenantId, ct),
            cancellationToken);

        var agent = agents.SingleOrDefault(candidate => candidate.Id == agentId)
            ?? throw new AiAgentNotFoundException();

        // Checked only when this is the last one, the same way ORB-A08 only counts owners
        // when the target is an owner.
        if (agents.Count == 1)
        {
            throw new CannotDeleteLastAgentException();
        }

        // An assistant that has run is history, not configuration. Checked here rather
        // than left to the RESTRICT foreign key on ai_runs, which would surface as an
        // unhandled constraint violation — a 500 on a button the screen offers.
        var hasHistory = await unitOfWork.QueryInTenantScopeAsync(
            ct => runRepository.ExistsForAgentAsync(tenantId, agentId, ct),
            cancellationToken);

        if (hasHistory)
        {
            throw new AgentHasHistoryException();
        }

        // Its draft, documents and chunks go with it: every one of those cascades from the
        // agent row.
        agentRepository.Remove(agent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public IReadOnlyList<AiTool> ListTools() => AiToolCatalog.All;

    private async Task<(AiAgent Agent, AiAgentDraft? Draft)> LoadAsync(
        Guid tenantId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var loaded = await unitOfWork.QueryInTenantScopeAsync(
            async ct => (
                Agent: await agentRepository.GetByIdAsync(tenantId, agentId, ct),
                Draft: await draftRepository.GetByAgentAsync(tenantId, agentId, ct)),
            cancellationToken);

        return loaded.Agent is null
            ? throw new AiAgentNotFoundException()
            : (loaded.Agent, loaded.Draft);
    }
}
