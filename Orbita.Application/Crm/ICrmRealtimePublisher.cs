namespace Orbita.Application.Crm;

/// <summary>
/// Pushes kanban changes to every open dashboard in the tenant (ORB-D05).
/// Implementations must be safe to call from inside the same unit-of-work method
/// after SaveChanges — they are a side effect, not part of the transaction.
/// </summary>
public interface ICrmRealtimePublisher
{
    Task PublishOpportunityChangedAsync(Guid tenantId, OpportunityChangedEvent change, CancellationToken cancellationToken);
}
