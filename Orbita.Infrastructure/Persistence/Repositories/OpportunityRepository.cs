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

    public Task<Opportunity?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Opportunities.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Opportunity>> GetByPipelineAsync(
        Guid pipelineId,
        Guid? assignedToUserId,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Opportunities.Where(o => o.PipelineId == pipelineId);
        if (assignedToUserId is not null)
        {
            query = query.Where(o => o.AssignedToUserId == assignedToUserId);
        }

        if (createdFrom is not null)
        {
            query = query.Where(o => o.CreatedAt >= createdFrom);
        }

        if (createdTo is not null)
        {
            query = query.Where(o => o.CreatedAt <= createdTo);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Opportunity opportunity, CancellationToken cancellationToken)
        => await dbContext.Opportunities.AddAsync(opportunity, cancellationToken);
}
