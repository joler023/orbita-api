using Moq;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

/// <summary>
/// ORB-C12. The cache is the one place in the product that answers a customer with words
/// generated for somebody else, so these tests are mostly about what it refuses to reuse.
/// </summary>
public sealed class AgentAnswerCacheTests
{
    private readonly Mock<IAgentAnswerCacheRepository> _entries = new();
    private readonly Mock<IKnowledgeDocumentRepository> _documents = new();
    private readonly Mock<ILlmProvider> _llm = new();
    private readonly Mock<IAiRunRecorder> _runs = new();
    private readonly PassThroughUnitOfWork _unitOfWork = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly AgentAnswerCache _sut;

    private static readonly float[] Vector = [.. Enumerable.Repeat(0.01f, KnowledgeChunk.EmbeddingDimensions)];

    public AgentAnswerCacheTests()
    {
        _llm
            .Setup(p => p.EmbedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmEmbeddingResult(Vector, new LlmUsage("openai/text-embedding-3-small", 12, 0, 0.0000002m, 90, null)));

        _documents
            .Setup(d => d.SummarizeForAgentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((3, _time.GetUtcNow()));

        _sut = new AgentAnswerCache(_entries.Object, _documents.Object, _llm.Object, _runs.Object, _unitOfWork, _time);
    }

    private AiAgent Agent(decimal? threshold = 0.95m)
    {
        var agent = AiAgent.Create(_tenantId, "Espiga", "Cercano.", "Ayuda.", AgentStyle.Default, _time.GetUtcNow());
        agent.SetSemanticCacheThreshold(threshold);

        return agent;
    }

    [Fact]
    public async Task A_lookup_asks_for_the_current_configuration_and_nothing_older()
    {
        var agent = Agent();
        string? asked = null;

        _entries
            .Setup(e => e.FindSimilarAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<float>>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid _, IReadOnlyList<float> _, string fingerprint, double _, CancellationToken _) => asked = fingerprint)
            .ReturnsAsync((AgentAnswerCacheHit?)null);

        var lookup = await _sut.LookupAsync(_tenantId, agent, _conversationId, "¿a qué hora abren?", CancellationToken.None);

        Assert.Equal(AgentAnswerFingerprint.Compute(agent, 3, _time.GetUtcNow()), asked);
        Assert.Equal(asked, lookup.Fingerprint);
    }

    [Fact]
    public async Task A_hit_and_a_miss_are_told_apart_in_the_ledger()
    {
        // The hit rate ORB-C12 promises is read straight off ai_runs, so the two lookups
        // have to be distinguishable there — there is no counter anywhere else.
        var agent = Agent();

        _entries
            .Setup(e => e.FindSimilarAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<float>>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentAnswerCacheHit(Guid.NewGuid(), "Abrimos de 7 a 7.", 0.98));

        await _sut.LookupAsync(_tenantId, agent, _conversationId, "¿a qué hora abren?", CancellationToken.None);

        _runs.Verify(
            r => r.Record(
                _tenantId,
                agent.Id,
                It.Is<LlmUsage>(u => u.FinishReason == AgentAnswerCache.HitReason),
                _conversationId,
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<IReadOnlyList<Guid>?>()),
            Times.Once);

        _entries
            .Setup(e => e.FindSimilarAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<float>>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentAnswerCacheHit?)null);

        await _sut.LookupAsync(_tenantId, agent, _conversationId, "¿a qué hora abren?", CancellationToken.None);

        _runs.Verify(
            r => r.Record(
                _tenantId,
                agent.Id,
                It.Is<LlmUsage>(u => u.FinishReason == AgentAnswerCache.MissReason),
                _conversationId,
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<IReadOnlyList<Guid>?>()),
            Times.Once);
    }

    [Fact]
    public async Task The_threshold_the_owner_set_is_the_one_the_search_uses()
    {
        var agent = Agent(0.88m);
        double? used = null;

        _entries
            .Setup(e => e.FindSimilarAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<float>>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid _, IReadOnlyList<float> _, string _, double threshold, CancellationToken _) => used = threshold)
            .ReturnsAsync((AgentAnswerCacheHit?)null);

        await _sut.LookupAsync(_tenantId, agent, _conversationId, "¿a qué hora abren?", CancellationToken.None);

        Assert.Equal(0.88, used);
    }

    [Fact]
    public async Task An_assistant_with_the_cache_off_is_a_programming_error_not_a_miss()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.LookupAsync(_tenantId, Agent(threshold: null), _conversationId, "hola", CancellationToken.None));

    [Fact]
    public void A_stored_answer_carries_the_fingerprint_it_was_generated_under()
    {
        var agent = Agent();
        AgentAnswerCacheEntry? stored = null;
        _entries.Setup(e => e.Add(It.IsAny<AgentAnswerCacheEntry>())).Callback((AgentAnswerCacheEntry e) => stored = e);

        _sut.Store(
            _tenantId,
            agent,
            new AgentCacheLookup(null, Vector, "abc123", Guid.NewGuid()),
            "  ¿a qué hora abren?  ",
            "Abrimos de 7 a 7.");

        Assert.Equal("abc123", stored!.Fingerprint);
        Assert.Equal("¿a qué hora abren?", stored.Question);
        Assert.Equal(agent.Id, stored.AgentId);
        Assert.Equal(_tenantId, stored.TenantId);
    }
}
