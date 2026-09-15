using Orbita.Application.Identity;
using Orbita.Domain.Ai;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;

namespace Orbita.Application.Ai;

public sealed class KnowledgeSearchService(
    IAiAgentRepository agentRepository,
    IKnowledgeChunkRepository chunkRepository,
    ILlmProvider llmProvider,
    IAiRunRecorder runRecorder,
    ITenantAuthorizationService authorizationService,
    IUnitOfWork unitOfWork) : IKnowledgeSearchService
{
    public const int DefaultLimit = 5;

    public const int MaxLimit = 20;

    public async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        Guid tenantId,
        Guid callerUserId,
        Guid agentId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        await authorizationService.EnsurePermissionAsync(tenantId, callerUserId, Permission.ManageAiAgents, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var agent = await unitOfWork.QueryInTenantScopeAsync(
            ct => agentRepository.GetByIdAsync(tenantId, agentId, ct),
            cancellationToken);

        if (agent is null)
        {
            throw new AiAgentNotFoundException();
        }

        // The question is embedded with the same model the chunks were, because vectors
        // from different models are not comparable at all — a mismatch does not degrade
        // results, it makes them meaningless.
        var embedding = await llmProvider.EmbedAsync(query, tenantId, cancellationToken);

        var hits = await unitOfWork.QueryInTenantScopeAsync(
            ct => chunkRepository.SearchAsync(
                tenantId,
                agentId,
                embedding.Vector,
                Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit),
                ct),
            cancellationToken);

        // Searching costs tokens like any other model call, so it is measured like one.
        // Recorded after the search rather than before, so a search that failed to run is
        // not billed as one that did. SaveChangesAsync establishes the tenant scope
        // itself, so the staged run lands under the right RLS session.
        runRecorder.Record(tenantId, agentId, embedding.Usage);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return hits;
    }
}
