namespace Orbita.Domain.Crm;

public interface IOpportunityRepository
{
    Task<int> CountByStageAsync(Guid stageId, CancellationToken cancellationToken);

    Task<int> CountByPipelineAsync(Guid pipelineId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Opportunity>> GetByStageAsync(Guid stageId, CancellationToken cancellationToken);

    Task<Opportunity?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Opportunity>> GetByContactAsync(Guid contactId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Opportunity>> GetByContactIdsAsync(
        IReadOnlyCollection<Guid> contactIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Opportunity>> GetByPipelineAsync(
        Guid pipelineId,
        Guid? assignedToUserId,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        CancellationToken cancellationToken);

    /// <summary>ORB-D13: tenant-wide dump for CSV export.</summary>
    Task<IReadOnlyList<Opportunity>> ListForExportAsync(Guid tenantId, int limit, CancellationToken cancellationToken);

    Task AddAsync(Opportunity opportunity, CancellationToken cancellationToken);
}
