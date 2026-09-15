using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// One piece of a streamed reply. Streaming exists so ORB-C11's test bench and, later,
/// the dashboard can show text as it is produced instead of blocking for the whole
/// reply.
/// </summary>
/// <param name="DeltaContent">Text produced since the previous chunk. Null on the final chunk.</param>
/// <param name="ToolCall">
/// A tool call that finished assembling in this chunk. Providers stream tool arguments
/// in fragments; adapters buffer those and only emit here once a call is complete, so
/// consumers never see half-parsed arguments.
/// </param>
/// <param name="IsFinal">True exactly once, on the last chunk of the stream.</param>
/// <param name="Usage">
/// Populated only on the final chunk — token counts are not known until generation
/// ends. Null on every other chunk.
/// </param>
public sealed record LlmChunk(
    string? DeltaContent,
    LlmToolCall? ToolCall,
    bool IsFinal,
    LlmUsage? Usage);
