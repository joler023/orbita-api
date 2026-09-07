using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

public sealed class AiRunRecorder(IAiRunRepository runRepository, TimeProvider timeProvider) : IAiRunRecorder
{
    public void Record(Guid tenantId, Guid agentId, LlmUsage usage, Guid? conversationId = null)
    {
        ArgumentNullException.ThrowIfNull(usage);

        runRepository.Add(AiRun.Record(
            tenantId,
            agentId,
            conversationId,
            usage.Model,
            usage.TokensIn,
            usage.TokensOut,
            usage.CostUsd,
            usage.LatencyMs,
            usage.FinishReason,
            timeProvider.GetUtcNow()));
    }

    public void RecordFailure(Guid tenantId, Guid agentId, string model, string error, int? latencyMs, Guid? conversationId = null)
        => runRepository.Add(AiRun.RecordFailure(
            tenantId,
            agentId,
            conversationId,
            model,
            error,
            latencyMs,
            timeProvider.GetUtcNow()));
}
