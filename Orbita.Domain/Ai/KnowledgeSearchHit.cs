namespace Orbita.Domain.Ai;

/// <summary>
/// One chunk that matched a semantic search, with enough context to cite it.
///
/// ORB-C03 requires hits to carry "su puntaje y su fuente" — the score so callers can
/// decide whether a match is good enough to use, and the source because ORB-C11's test
/// bench has to show an operator <em>where</em> an answer came from, and ORB-C04's agent
/// must never present retrieved text as if it were its own knowledge.
/// </summary>
/// <param name="Score">
/// Cosine similarity in [0, 1], where 1 is identical. pgvector's <c>&lt;=&gt;</c> operator
/// returns cosine <em>distance</em>; this is the similarity, because "higher is better"
/// is what every caller and every UI expects, and getting that backwards silently ranks
/// the worst match first.
/// </param>
public sealed record KnowledgeSearchHit(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    int ChunkIndex,
    string Content,
    double Score);
