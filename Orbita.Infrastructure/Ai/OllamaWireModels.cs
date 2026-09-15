using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orbita.Infrastructure.Ai;

// Wire models for Ollama's own HTTP API (/api/chat, /api/embed). Every property is
// named explicitly with [JsonPropertyName] rather than relying on a naming policy —
// these mirror somebody else's contract, so they should be readable next to that
// provider's documentation and immune to a serializer-wide policy change.
//
// `JsonElement` appears for two fields that are genuinely arbitrary JSON: a tool's
// JSON Schema, and the arguments a model passes to a tool. CLAUDE.md's ban on
// `object`/`JsonElement` catch-alls is about the public API surface (controller DTOs);
// here the payload really is caller-defined JSON being forwarded verbatim.

internal sealed class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OllamaRequestMessage> Messages { get; init; }

    [JsonPropertyName("stream")]
    public required bool Stream { get; init; }

    [JsonPropertyName("options")]
    public required OllamaOptions Options { get; init; }

    /// <summary>Omitted entirely when the agent has no tools enabled — an empty array is not the same thing.</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OllamaTool>? Tools { get; init; }
}

internal sealed class OllamaOptions
{
    [JsonPropertyName("temperature")]
    public required decimal Temperature { get; init; }

    /// <summary>Ollama's name for the max-tokens cap.</summary>
    [JsonPropertyName("num_predict")]
    public required int NumPredict { get; init; }
}

internal sealed class OllamaRequestMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

internal sealed class OllamaTool
{
    [JsonPropertyName("type")]
    public string Type => "function";

    [JsonPropertyName("function")]
    public required OllamaToolFunction Function { get; init; }
}

internal sealed class OllamaToolFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("parameters")]
    public required JsonElement Parameters { get; init; }
}

internal sealed class OllamaChatResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("message")]
    public OllamaResponseMessage? Message { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; init; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; init; }
}

internal sealed class OllamaResponseMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<OllamaResponseToolCall>? ToolCalls { get; init; }
}

internal sealed class OllamaResponseToolCall
{
    [JsonPropertyName("function")]
    public OllamaResponseToolCallFunction? Function { get; init; }
}

internal sealed class OllamaResponseToolCallFunction
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Ollama returns tool arguments as a JSON <em>object</em>, where OpenAI-shaped APIs
    /// return a JSON <em>string</em>. The adapter normalizes this to the string form so
    /// callers never branch on which provider answered.
    /// </summary>
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; init; }
}

internal sealed class OllamaEmbedRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input")]
    public required string Input { get; init; }
}

internal sealed class OllamaEmbedResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>One vector per input; this adapter always sends exactly one input.</summary>
    [JsonPropertyName("embeddings")]
    public IReadOnlyList<IReadOnlyList<float>>? Embeddings { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; init; }
}
