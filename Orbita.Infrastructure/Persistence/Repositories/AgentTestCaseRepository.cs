using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class AgentTestCaseRepository(OrbitaDbContext dbContext) : IAgentTestCaseRepository
{
    public async Task<IReadOnlyList<AgentTestCase>> ListByAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
        => await dbContext.AgentTestCases
            .Where(c => c.TenantId == tenantId && c.AgentId == agentId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<AgentTestCase?> GetByIdAsync(Guid tenantId, Guid testCaseId, CancellationToken cancellationToken)
        => dbContext.AgentTestCases.SingleOrDefaultAsync(c => c.Id == testCaseId && c.TenantId == tenantId, cancellationToken);

    public Task<int> CountByAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
        => dbContext.AgentTestCases.CountAsync(c => c.TenantId == tenantId && c.AgentId == agentId, cancellationToken);

    public void Add(AgentTestCase testCase) => dbContext.AgentTestCases.Add(testCase);

    public void Remove(AgentTestCase testCase) => dbContext.AgentTestCases.Remove(testCase);
}
