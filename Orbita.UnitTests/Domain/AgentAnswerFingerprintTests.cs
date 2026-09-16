using Orbita.Domain.Ai;

namespace Orbita.UnitTests.Domain;

/// <summary>
/// ORB-C12's invalidation, which is the whole safety story of the cache: an answer is
/// only reused while everything that shaped it is unchanged. Each of these is a way the
/// assistant would answer differently today — and so a way a stale answer would be wrong.
/// </summary>
public sealed class AgentAnswerFingerprintTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static AiAgent Agent()
        => AiAgent.Create(Guid.NewGuid(), "Espiga", "Cercano.", "Confirma la hora de recogida.", AgentStyle.Default, Now);

    [Fact]
    public void The_same_assistant_over_the_same_documents_keeps_its_answers()
    {
        var agent = Agent();

        Assert.Equal(AgentAnswerFingerprint.Compute(agent, 4, Now), AgentAnswerFingerprint.Compute(agent, 4, Now));
    }

    [Fact]
    public void Uploading_reindexing_or_deleting_a_document_retires_every_earlier_answer()
    {
        var agent = Agent();
        var before = AgentAnswerFingerprint.Compute(agent, 4, Now);

        // One more document, the same documents re-indexed, one document less.
        Assert.NotEqual(before, AgentAnswerFingerprint.Compute(agent, 5, Now));
        Assert.NotEqual(before, AgentAnswerFingerprint.Compute(agent, 4, Now.AddMinutes(1)));
        Assert.NotEqual(before, AgentAnswerFingerprint.Compute(agent, 3, Now));
    }

    [Fact]
    public void Publishing_new_instructions_retires_them_too()
    {
        var agent = Agent();
        var before = AgentAnswerFingerprint.Compute(agent, 4, Now);

        agent.ApplyConfiguration("Espiga", "Cercano.", "Ahora cobramos domicilio.", AgentStyle.Default, agent.Tools);

        Assert.NotEqual(before, AgentAnswerFingerprint.Compute(agent, 4, Now));
    }

    [Fact]
    public void Blocking_a_topic_retires_them_as_well()
    {
        // Otherwise the first thing a blocked topic would do is keep answering from cache.
        var agent = Agent();
        var before = AgentAnswerFingerprint.Compute(agent, 4, Now);

        agent.SetGuardrails(["dosis"], AiAgent.DefaultOutOfScopeReply);

        Assert.NotEqual(before, AgentAnswerFingerprint.Compute(agent, 4, Now));
    }

    [Fact]
    public void An_assistant_with_no_documents_still_has_a_fingerprint()
        => Assert.NotEmpty(AgentAnswerFingerprint.Compute(Agent(), 0, null));

    [Theory]
    [InlineData(0.79)]
    [InlineData(1.0)]
    public void A_threshold_outside_the_useful_range_is_refused(decimal threshold)
    {
        // Below 0.80 "similar" starts meaning "same topic" and the customer gets the answer
        // to a question they did not ask; above 0.99 nothing ever matches.
        Assert.Throws<ArgumentOutOfRangeException>(() => Agent().SetSemanticCacheThreshold(threshold));
    }

    [Fact]
    public void The_cache_is_off_until_somebody_turns_it_on()
        => Assert.Null(Agent().SemanticCacheThreshold);
}
