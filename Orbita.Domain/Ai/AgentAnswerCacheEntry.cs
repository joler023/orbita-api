using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// ORB-C12: an answer the assistant already gave, kept so a near-identical question can
/// reuse it instead of paying for the model again.
///
/// Only answers that depended on nothing but the question are ever stored — the first
/// reply in a conversation, with no tool called. A reply shaped by earlier turns, or one
/// that registered an opportunity, is not an answer to the question; it is an answer to
/// that conversation, and replaying it to someone else would be wrong in a way no
/// similarity threshold can catch.
///
/// <see cref="Fingerprint"/> is what invalidates it: a hash of the assistant's
/// configuration and the state of its documents at the time. Any change to either
/// produces a different fingerprint, and an entry whose fingerprint no longer matches is
/// simply never returned — "se invalida al cambiar la base de conocimiento" without a
/// delete that would have to run inside the right tenant scope to do anything at all.
/// </summary>
public sealed class AgentAnswerCacheEntry : Entity
{
    public const int QuestionMaxLength = 2_000;

    private AgentAnswerCacheEntry(Guid id, Guid tenantId, Guid agentId, string question, float[] embedding, string answer, string fingerprint, DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        AgentId = agentId;
        Question = question;
        Embedding = embedding;
        Answer = answer;
        Fingerprint = fingerprint;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; }

    public Guid AgentId { get; }

    public string Question { get; }

    public float[] Embedding { get; }

    public string Answer { get; }

    public string Fingerprint { get; }

    public DateTimeOffset CreatedAt { get; }

    public static AgentAnswerCacheEntry Create(
        Guid tenantId, Guid agentId, string question, IReadOnlyList<float> embedding, string answer, string fingerprint, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(embedding);

        if (embedding.Count != KnowledgeChunk.EmbeddingDimensions)
        {
            throw new ArgumentException(
                $"Embedding has {embedding.Count} dimensions but the cache stores {KnowledgeChunk.EmbeddingDimensions}.", nameof(embedding));
        }

        var trimmed = question.Trim();

        return new AgentAnswerCacheEntry(
            Guid.NewGuid(), tenantId, agentId, trimmed.Length > QuestionMaxLength ? trimmed[..QuestionMaxLength] : trimmed,
            [.. embedding], answer, fingerprint, now);
    }
}

/// <summary>A cache hit, with how close the question was.</summary>
public sealed record AgentAnswerCacheHit(Guid EntryId, string Answer, double Score);

public interface IAgentAnswerCacheRepository
{
    /// <summary>
    /// The closest stored answer for this assistant whose fingerprint still matches and
    /// whose similarity reaches <paramref name="threshold"/>, or null.
    /// </summary>
    Task<AgentAnswerCacheHit?> FindSimilarAsync(
        Guid tenantId, Guid agentId, IReadOnlyList<float> embedding, string fingerprint, double threshold, CancellationToken cancellationToken);

    void Add(AgentAnswerCacheEntry entry);
}
