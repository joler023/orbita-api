namespace Orbita.Domain.Ai;

public interface IAiRunRepository
{
    /// <summary>
    /// Stages a run. Like <c>IAuditLogger</c>, this never saves on its own — the caller's
    /// own <c>SaveChangesAsync</c> commits it, so the run and the change it describes land
    /// in the same transaction and under the same RLS-scoped tenant.
    /// </summary>
    void Add(AiRun run);

    /// <summary>
    /// Whether this assistant has ever been run.
    ///
    /// Asked before deleting one: <c>ai_runs</c> is the consumption ledger ORB-A13's
    /// metering and ORB-A12's billing are computed from, so an assistant's history
    /// outlives the assistant on purpose and the foreign key is <c>RESTRICT</c>. Without
    /// this check the delete reaches Postgres and comes back as an unhandled constraint
    /// violation — a 500 on a button the configuration screen offers.
    /// </summary>
    Task<bool> ExistsForAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken);
}
