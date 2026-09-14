using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class AiAgentRepository(OrbitaDbContext dbContext) : IAiAgentRepository
{
    public void Add(AiAgent agent) => dbContext.AiAgents.Add(agent);

    public void Remove(AiAgent agent) => dbContext.AiAgents.Remove(agent);

    public Task<AiAgent?> GetByIdAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
        => dbContext.AiAgents.SingleOrDefaultAsync(a => a.Id == agentId && a.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<AiAgent>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => await dbContext.AiAgents
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
}
