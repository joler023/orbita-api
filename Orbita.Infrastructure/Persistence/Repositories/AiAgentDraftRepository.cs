using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class AiAgentDraftRepository(OrbitaDbContext dbContext) : IAiAgentDraftRepository
{
    public void Add(AiAgentDraft draft) => dbContext.AiAgentDrafts.Add(draft);

    public void Remove(AiAgentDraft draft) => dbContext.AiAgentDrafts.Remove(draft);

    public Task<AiAgentDraft?> GetByAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
        => dbContext.AiAgentDrafts
            .SingleOrDefaultAsync(draft => draft.Id == agentId && draft.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<AiAgentDraft>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => await dbContext.AiAgentDrafts
            .Where(draft => draft.TenantId == tenantId)
            .ToListAsync(cancellationToken);
}
