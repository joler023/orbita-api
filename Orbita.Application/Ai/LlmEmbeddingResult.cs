using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// A text turned into a vector, for the knowledge base (ORB-C02) and semantic search
/// (ORB-C03).
/// </summary>
/// <param name="Vector">
/// The embedding. Its length is the embedding model's dimension and must match the
/// <c>vector(n)</c> column <c>knowledge_chunks.embedding</c> was created with —
/// mismatched dimensions are a migration-level problem (they require a full reindex),
/// which is why the model is pinned by configuration rather than chosen per call.
/// </param>
/// <param name="Usage">What the call cost. <c>TokensOut</c> is always 0 — embeddings generate no text.</param>
public sealed record LlmEmbeddingResult(
    IReadOnlyList<float> Vector,
    LlmUsage Usage);

/// <summary>
/// Several texts embedded in one call.
/// </summary>
/// <param name="Vectors">
/// One per input, <b>in the order the inputs were given</b>. Callers line them up with
/// their own chunks by position, so a provider that returns them out of order has to be
/// reordered before it gets here — which is exactly what the OpenAI-compatible adapter
/// does with the response's <c>index</c> field.
/// </param>
/// <param name="Usage">
/// What the whole call cost, not one text's share. It is recorded as a single
/// <c>ai_runs</c> row for the same reason: one call is what was billed.
/// </param>
public sealed record LlmEmbeddingBatchResult(
    IReadOnlyList<IReadOnlyList<float>> Vectors,
    LlmUsage Usage);
