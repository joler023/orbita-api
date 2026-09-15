using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// Records what a model call consumed, into <c>ai_runs</c>.
///
/// Like ORB-A15's <c>IAuditLogger</c>, this only <em>stages</em> the row — it never saves.
/// Call it from inside the same service method whose own <c>SaveChangesAsync</c> persists
/// the work the call paid for, so the measurement and the result land in one transaction
/// under one tenant scope. A run committed separately could survive a rolled-back
/// indexing attempt and bill someone for chunks that do not exist.
/// </summary>
public interface IAiRunRecorder
{
    /// <summary>
    /// Returns the staged run's id so a caller that produces something attributable to
    /// the call — ORB-C04's reply, which carries <c>messages.ai_run_id</c> — can link the
    /// two in the same transaction. The row is staged, not saved: the id is real before
    /// anything reaches the database, because it is client-generated like every other
    /// <c>Guid</c> key here.
    /// </summary>
    Guid Record(
        Guid tenantId,
        Guid agentId,
        LlmUsage usage,
        Guid? conversationId = null,
        IReadOnlyList<string>? toolsCalled = null,
        IReadOnlyList<Guid>? retrievedChunkIds = null);

    /// <summary>A call that failed still consumed time, and often tokens.</summary>
    void RecordFailure(Guid tenantId, Guid agentId, string model, string error, int? latencyMs, Guid? conversationId = null);
}
