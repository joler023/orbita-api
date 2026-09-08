namespace Orbita.Application.Outbox;

/// <summary>
/// Stages an integration event (ORB-B04) — call this from inside the same application-
/// service method whose <c>IUnitOfWork.SaveChangesAsync</c> call persists the change
/// being described, so the event and the change land in the same transaction. This only
/// stages the entity via the repository; it never calls SaveChangesAsync itself, on
/// purpose — same pattern as <see cref="Audit.IAuditLogger"/>.
/// </summary>
public interface IOutboxWriter
{
    /// <param name="payload">
    /// Serialized to JSON as-is; pass an anonymous object of ids/enums, never PII
    /// (message bodies, names, phone numbers, emails) — this row can sit unpublished
    /// for a while and is read by handlers with no tenant scoping.
    /// </param>
    Task StageAsync(Guid tenantId, string aggregateType, Guid aggregateId, string eventType, object payload, CancellationToken cancellationToken);
}
