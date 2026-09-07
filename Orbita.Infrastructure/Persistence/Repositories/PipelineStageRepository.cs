using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Crm;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class PipelineStageRepository(OrbitaDbContext dbContext) : IPipelineStageRepository
{
    public Task<PipelineStage?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.PipelineStages.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PipelineStage>> GetByPipelineAsync(Guid pipelineId, CancellationToken cancellationToken)
        => await dbContext.PipelineStages
            .Where(s => s.PipelineId == pipelineId)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);

    public Task<int> CountByPipelineAsync(Guid pipelineId, CancellationToken cancellationToken)
        => dbContext.PipelineStages.CountAsync(s => s.PipelineId == pipelineId, cancellationToken);

    public async Task AddAsync(PipelineStage stage, CancellationToken cancellationToken)
        => await dbContext.PipelineStages.AddAsync(stage, cancellationToken);

    public void Remove(PipelineStage stage)
        => dbContext.PipelineStages.Remove(stage);
}
