namespace Orbita.Domain.Billing;

public interface IPlanRepository
{
    Task<IReadOnlyList<Plan>> GetAllActiveAsync(CancellationToken cancellationToken);

    Task<Plan?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
