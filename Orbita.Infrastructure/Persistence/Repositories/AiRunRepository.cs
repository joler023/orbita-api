using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class AiRunRepository(OrbitaDbContext dbContext) : IAiRunRepository
{
    public void Add(AiRun run) => dbContext.AiRuns.Add(run);

    public Task<bool> ExistsForAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
        => dbContext.AiRuns.AnyAsync(r => r.TenantId == tenantId && r.AgentId == agentId, cancellationToken);
}
