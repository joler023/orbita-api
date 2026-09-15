using Orbita.Domain.Ai;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class AiRunRepository(OrbitaDbContext dbContext) : IAiRunRepository
{
    public void Add(AiRun run) => dbContext.AiRuns.Add(run);
}
