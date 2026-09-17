using System.Runtime.CompilerServices;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.UnitTests.TestSupport;

/// <summary>
/// A scripted <see cref="ILlmProvider"/>, so a test can say "fail twice, then succeed"
/// without mocking an HTTP stack.
///
/// The <b>last</b> scripted outcome repeats forever once the script runs out. That is
/// deliberate: a provider that is down stays down, so <c>new StubLlmProvider("x", Transient)</c>
/// means "always failing" rather than "fails once and then quietly recovers", which
/// would make a failover test pass for the wrong reason.
/// </summary>
internal sealed class StubLlmProvider(string name, params Func<LlmProviderException?>[] outcomes) : ILlmProvider
{
    private readonly IReadOnlyList<Func<LlmProviderException?>> _outcomes = outcomes;
    private int _index;

    public string Name { get; } = name;

    public bool IsConfigured { get; init; } = true;

    /// <summary>How many times any method on this provider was actually invoked.</summary>
    public int CallCount { get; private set; }

    /// <summary>Chunks handed out by <see cref="StreamAsync"/> before its scripted outcome applies.</summary>
    public IReadOnlyList<string> ChunksBeforeFailure { get; init; } = [];

    public static StubLlmProvider AlwaysSucceeds(string name) => new(name, () => null);

    public static LlmProviderException Transient(string provider)
        => new(provider, "provider is down", isTransient: true);

    public static LlmProviderException Permanent(string provider)
        => new(provider, "unknown model", isTransient: false);

    public Task<LlmCompletionResult> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken)
    {
        CallCount++;

        if (NextOutcome() is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(new LlmCompletionResult("ok", [], UsageFor("stub-model")));
    }

    public Task<LlmEmbeddingResult> EmbedAsync(string text, Guid tenantId, CancellationToken cancellationToken)
    {
        CallCount++;

        if (NextOutcome() is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(new LlmEmbeddingResult([0.1f, 0.2f], UsageFor("stub-embed")));
    }

    public Task<LlmEmbeddingBatchResult> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        CallCount++;

        if (NextOutcome() is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(new LlmEmbeddingBatchResult(
            [.. texts.Select(IReadOnlyList<float> (_) => [0.1f, 0.2f])],
            UsageFor("stub-embed")));
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CallCount++;

        foreach (var chunk in ChunksBeforeFailure)
        {
            yield return new LlmChunk(chunk, null, IsFinal: false, Usage: null);
        }

        if (NextOutcome() is { } failure)
        {
            throw failure;
        }

        yield return new LlmChunk(null, null, IsFinal: true, Usage: UsageFor("stub-model"));
        await Task.CompletedTask;
    }

    private LlmProviderException? NextOutcome()
    {
        if (_outcomes.Count == 0)
        {
            return null;
        }

        var outcome = _outcomes[Math.Min(_index, _outcomes.Count - 1)];
        _index++;
        return outcome();
    }

    private LlmUsage UsageFor(string model) => new(model, 10, 5, 0m, 1, "stop");
}
