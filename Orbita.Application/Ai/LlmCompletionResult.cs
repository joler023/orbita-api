using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>A completed (non-streamed) model reply.</summary>
/// <param name="Content">
/// The generated text. Null or empty when the model only asked for tools — that is a
/// normal outcome, not a failure.
/// </param>
/// <param name="ToolCalls">Tools the model wants run. Empty when it just answered.</param>
/// <param name="Usage">What the call cost, always populated (ORB-C01).</param>
public sealed record LlmCompletionResult(
    string? Content,
    IReadOnlyList<LlmToolCall> ToolCalls,
    LlmUsage Usage);
