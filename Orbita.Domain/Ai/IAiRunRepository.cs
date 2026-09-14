namespace Orbita.Domain.Ai;

public interface IAiRunRepository
{
    /// <summary>
    /// Stages a run. Like <c>IAuditLogger</c>, this never saves on its own — the caller's
    /// own <c>SaveChangesAsync</c> commits it, so the run and the change it describes land
    /// in the same transaction and under the same RLS-scoped tenant.
    /// </summary>
    void Add(AiRun run);
}
