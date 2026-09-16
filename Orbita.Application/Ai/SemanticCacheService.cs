using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Ai;

/// <summary>ORB-C12's settings screen: the threshold, and how often it has paid off.</summary>
public interface ISemanticCacheService
{
    /// <exception cref="AiAgentNotFoundException"/>
    Task<SemanticCacheDto> GetAsync(Guid tenantId, Guid callerUserId, Guid agentId, CancellationToken cancellationToken);

    /// <summary><see cref="SemanticCacheLevel.Off"/> turns the cache off.</summary>
    /// <exception cref="AiAgentNotFoundException"/>
    Task<SemanticCacheDto> SetLevelAsync(
        Guid tenantId, Guid callerUserId, Guid agentId, SemanticCacheLevel level, CancellationToken cancellationToken);
}

/// <param name="Level">
/// How freely the assistant reuses answers. The similarity threshold behind it is never
/// part of this contract — see <see cref="SemanticCacheLevel"/> for why.
/// </param>
/// <param name="HitRate">Hits over lookups, or null before the first lookup — 0% and "never asked" are different answers.</param>
public sealed record SemanticCacheDto(SemanticCacheLevel Level, int Hits, int Misses, decimal? HitRate);

public sealed class SemanticCacheService(
    IAiAgentRepository agents,
    IAiRunRepository runs,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork) : ISemanticCacheService
{
    public async Task<SemanticCacheDto> GetAsync(Guid tenantId, Guid callerUserId, Guid agentId, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        return await unitOfWork.QueryInTenantScopeAsync(
            async ct =>
            {
                var agent = await agents.GetByIdAsync(tenantId, agentId, ct) ?? throw new AiAgentNotFoundException();

                return await BuildAsync(tenantId, agent, ct);
            },
            cancellationToken);
    }

    public async Task<SemanticCacheDto> SetLevelAsync(
        Guid tenantId, Guid callerUserId, Guid agentId, SemanticCacheLevel level, CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);

        AiAgent? agent = null;

        await unitOfWork.ExecuteAndSaveInTenantScopeAsync(
            async ct =>
            {
                agent = await agents.GetByIdAsync(tenantId, agentId, ct) ?? throw new AiAgentNotFoundException();
                agent.SetSemanticCacheLevel(level);
            },
            cancellationToken);

        return await unitOfWork.QueryInTenantScopeAsync(ct => BuildAsync(tenantId, agent!, ct), cancellationToken);
    }

    private async Task<SemanticCacheDto> BuildAsync(Guid tenantId, AiAgent agent, CancellationToken cancellationToken)
    {
        var hits = await runs.CountByFinishReasonAsync(tenantId, agent.Id, AgentAnswerCache.HitReason, cancellationToken);
        var misses = await runs.CountByFinishReasonAsync(tenantId, agent.Id, AgentAnswerCache.MissReason, cancellationToken);
        var lookups = hits + misses;

        return new SemanticCacheDto(
            agent.SemanticCacheLevel, hits, misses, lookups == 0 ? null : Math.Round((decimal)hits / lookups, 4));
    }
}
