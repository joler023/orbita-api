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
    void Record(Guid tenantId, Guid agentId, LlmUsage usage, Guid? conversationId = null);

    /// <summary>A call that failed still consumed time, and often tokens.</summary>
    void RecordFailure(Guid tenantId, Guid agentId, string model, string error, int? latencyMs, Guid? conversationId = null);
}
