using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Crm;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class OpportunityRepository(OrbitaDbContext dbContext) : IOpportunityRepository
{
    public Task<int> CountByStageAsync(Guid stageId, CancellationToken cancellationToken)
        => dbContext.Opportunities.CountAsync(o => o.StageId == stageId, cancellationToken);

    public Task<int> CountByPipelineAsync(Guid pipelineId, CancellationToken cancellationToken)
        => dbContext.Opportunities.CountAsync(o => o.PipelineId == pipelineId, cancellationToken);

    public async Task<IReadOnlyList<Opportunity>> GetByStageAsync(Guid stageId, CancellationToken cancellationToken)
        => await dbContext.Opportunities.Where(o => o.StageId == stageId).ToListAsync(cancellationToken);

    public async Task AddAsync(Opportunity opportunity, CancellationToken cancellationToken)
        => await dbContext.Opportunities.AddAsync(opportunity, cancellationToken);
}
