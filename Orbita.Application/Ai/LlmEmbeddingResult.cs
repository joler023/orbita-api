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
