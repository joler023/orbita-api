using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orbita.Infrastructure.Ai;

// Wire models for the OpenAI chat-completions shape (/v1/chat/completions,
// /v1/embeddings). See OpenAiCompatibleLlmProvider for why this shape specifically.

internal sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OpenAiRequestMessage> Messages { get; init; }

    [JsonPropertyName("temperature")]
    public required decimal Temperature { get; init; }

    [JsonPropertyName("max_tokens")]
    public required int MaxTokens { get; init; }

    [JsonPropertyName("stream")]
    public required bool Stream { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OpenAiTool>? Tools { get; init; }

    /// <summary>Asks the server to include token counts on the final SSE frame; ignored by servers that don't support it.</summary>
    [JsonPropertyName("stream_options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiStreamOptions? StreamOptions { get; init; }
}

internal sealed class OpenAiStreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool IncludeUsage => true;
}

internal sealed class OpenAiRequestMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>Required on a tool result, so the server can pair it with the call it answers.</summary>
    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; init; }

    /// <summary>On an assistant turn that asked for tools; see LlmMessage.AssistantToolCalls.</summary>
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAiRequestToolCall>? ToolCalls { get; init; }
}

internal sealed class OpenAiRequestToolCall
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public required OpenAiRequestToolCallFunction Function { get; init; }
}

internal sealed class OpenAiRequestToolCallFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A JSON string, as the format requires on the way in too.</summary>
    [JsonPropertyName("arguments")]
    public required string Arguments { get; init; }
}

internal sealed class OpenAiTool
{
    [JsonPropertyName("type")]
    public string Type => "function";

    [JsonPropertyName("function")]
    public required OpenAiToolFunction Function { get; init; }
}

internal sealed class OpenAiToolFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("parameters")]
    public required JsonElement Parameters { get; init; }
}

internal sealed class OpenAiChatResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("choices")]
    public IReadOnlyList<OpenAiChoice>? Choices { get; init; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; init; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiResponseMessage? Message { get; init; }

    /// <summary>Present on streamed frames instead of <see cref="Message"/>.</summary>
    [JsonPropertyName("delta")]
    public OpenAiResponseMessage? Delta { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

internal sealed class OpenAiResponseMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<OpenAiResponseToolCall>? ToolCalls { get; init; }
}

internal sealed class OpenAiResponseToolCall
{
    /// <summary>Null on streamed fragments after the first one for the same call.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Which call this fragment belongs to when streaming; absent on complete responses.</summary>
    [JsonPropertyName("index")]
    public int? Index { get; init; }

    [JsonPropertyName("function")]
    public OpenAiResponseToolCallFunction? Function { get; init; }
}

internal sealed class OpenAiResponseToolCallFunction
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>A JSON <em>string</em> here, unlike Ollama, which sends an object.</summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }
}

internal sealed class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }
}

internal sealed class OpenAiEmbeddingRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>
    /// Always an array, even for one text: the endpoint accepts both, and sending the
    /// same shape either way means the batch path and the single path cannot drift.
    /// </summary>
    [JsonPropertyName("input")]
    public required IReadOnlyList<string> Input { get; init; }
}

internal sealed class OpenAiEmbeddingResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("data")]
    public IReadOnlyList<OpenAiEmbeddingData>? Data { get; init; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; init; }
}

internal sealed class OpenAiEmbeddingData
{
    [JsonPropertyName("embedding")]
    public IReadOnlyList<float>? Embedding { get; init; }

    /// <summary>
    /// Which input this vector belongs to. The spec does not promise the array comes back
    /// in order, and a batch lined up by position would silently attach every chunk's text
    /// to another chunk's vector — a corpus that looks indexed and answers nonsense.
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }
}
