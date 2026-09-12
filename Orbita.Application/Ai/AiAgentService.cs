using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Ai;

public sealed class AiAgentService(
    IAiAgentRepository agentRepository,
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

        var agents = await unitOfWork.QueryInTenantScopeAsync(
            ct => agentRepository.ListByTenantAsync(tenantId, ct),
            cancellationToken);

        return agents.Select(agent => AiAgentDto.From(agent)).ToList();
    }

    public async Task<AiAgentDto> GetAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        return AiAgentDto.From(await RequireAgentAsync(tenantId, agentId, cancellationToken));
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
            tenantId, request.Name, request.Personality, request.Instructions, request.Tone, timeProvider.GetUtcNow());

        agent.EnableTools(request.Tools);

        agentRepository.Add(agent);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AiAgentDto.From(agent);
    }

    public async Task<AiAgentDto> UpdateAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        SaveAiAgentRequest request,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);

        var agent = await RequireAgentAsync(tenantId, agentId, cancellationToken);

        agent.Rename(request.Name);
        agent.Reconfigure(request.Personality, request.Instructions, request.Tone);
        agent.EnableTools(request.Tools);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AiAgentDto.From(agent);
    }

    public async Task<AiAgentDto> SetEnabledAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        var agent = await RequireAgentAsync(tenantId, agentId, cancellationToken);

        agent.SetEnabled(isEnabled);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AiAgentDto.From(agent);
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

        // Its documents and chunks go with it: knowledge_docs.agent_id cascades, and
        // knowledge_chunks cascades from the document.
        agentRepository.Remove(agent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public IReadOnlyList<AiTool> ListTools() => AiToolCatalog.All;

    private async Task<AiAgent> RequireAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await unitOfWork.QueryInTenantScopeAsync(
            ct => agentRepository.GetByIdAsync(tenantId, agentId, ct),
            cancellationToken);

        return agent ?? throw new AiAgentNotFoundException();
    }
}
