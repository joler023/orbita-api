using System.Runtime.CompilerServices;
using Orbita.Domain.Ai;

namespace Orbita.Application.Ai;

/// <summary>
/// ORB-C01's "reintento con retroceso, y conmutación a proveedor alterno si uno se cae",
/// as a decorator rather than a base class: the adapters stay ignorant of retries and
/// failover, and this type stays ignorant of HTTP. It is what the rest of the
/// application resolves when it asks for an <see cref="ILlmProvider"/>.
///
/// The decision it makes on a failure is driven entirely by
/// <see cref="LlmProviderException.IsTransient"/>:
///
/// <list type="bullet">
///   <item>
///     Transient (the provider timed out, refused the connection, or answered 5xx/429)
///     — retry the same provider with exponential backoff, then move to the next one.
///   </item>
///   <item>
///     Permanent (a 4xx: unknown model, malformed tool schema, oversized context) —
///     rethrow immediately. Retrying returns the same answer and so does asking a
///     different provider, because the request itself is what is wrong. Burning the
///     fallback chain on it would only turn one fast error into several slow ones.
///   </item>
/// </list>
/// </summary>
public sealed class ResilientLlmProvider : ILlmProvider
{
    private const int DefaultMaxAttemptsPerProvider = 3;
    private static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromMilliseconds(250);

    private readonly IReadOnlyList<ILlmProvider> _providers;
    private readonly int _maxAttemptsPerProvider;
    private readonly TimeSpan _initialBackoff;

    /// <param name="providers">
    /// Candidates in failover order, primary first. Unconfigured ones are dropped here
    /// rather than tried and failed, so a provider with no API key costs nothing at
    /// runtime — the same posture ORB-A12 takes with Stripe and Wompi.
    /// </param>
    /// <param name="maxAttemptsPerProvider">Total attempts per provider, including the first.</param>
    /// <param name="initialBackoff">
    /// Delay before the second attempt; each further attempt doubles it. Tests pass
    /// <see cref="TimeSpan.Zero"/> so they exercise the policy without waiting on it.
    /// </param>
    public ResilientLlmProvider(
        IEnumerable<ILlmProvider> providers,
        int maxAttemptsPerProvider = DefaultMaxAttemptsPerProvider,
        TimeSpan? initialBackoff = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttemptsPerProvider, 1);

        _providers = providers.Where(provider => provider.IsConfigured).ToList();
        _maxAttemptsPerProvider = maxAttemptsPerProvider;
        _initialBackoff = initialBackoff ?? DefaultInitialBackoff;
    }

    public string Name => "resilient";

    public bool IsConfigured => _providers.Count > 0;

    /// <summary>The providers actually in play, in failover order — exposed for diagnostics and tests.</summary>
    public IReadOnlyList<string> ProviderNames => _providers.Select(provider => provider.Name).ToList();

    public Task<LlmCompletionResult> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(provider => provider.CompleteAsync(request, cancellationToken), cancellationToken);

    public Task<LlmEmbeddingResult> EmbedAsync(string text, Guid tenantId, CancellationToken cancellationToken)
        => ExecuteAsync(provider => provider.EmbedAsync(text, tenantId, cancellationToken), cancellationToken);

    public Task<LlmEmbeddingBatchResult> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        Guid tenantId,
        CancellationToken cancellationToken)
        => ExecuteAsync(provider => provider.EmbedBatchAsync(texts, tenantId, cancellationToken), cancellationToken);

    /// <summary>
    /// Failover applies only up to the first chunk handed to the caller. Once any text
    /// has been yielded, a mid-stream failure propagates: restarting on another provider
    /// would replay a different reply on top of one the caller has already seen.
    /// </summary>
    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureAnyProviderConfigured();

        LlmProviderException? lastFailure = null;

        foreach (var provider in _providers)
        {
            for (var attempt = 1; attempt <= _maxAttemptsPerProvider; attempt++)
            {
                var enumerator = provider.StreamAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
                var yieldedAnything = false;

                try
                {
                    while (true)
                    {
                        LlmChunk chunk;

                        // No `yield` inside this try — C# forbids yielding from a try
                        // that has a catch clause, so the chunk is captured here and
                        // yielded below.
                        try
                        {
                            if (!await enumerator.MoveNextAsync())
                            {
                                yield break;
                            }

                            chunk = enumerator.Current;
                        }
                        catch (LlmProviderException failure) when (failure.IsTransient && !yieldedAnything)
                        {
                            lastFailure = failure;
                            break;
                        }

                        yieldedAnything = true;
                        yield return chunk;
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync();
                }

                if (attempt < _maxAttemptsPerProvider)
                {
                    await Task.Delay(BackoffFor(attempt), cancellationToken);
                }
            }
        }

        throw lastFailure ?? AllProvidersFailed();
    }

    private async Task<TResult> ExecuteAsync<TResult>(
        Func<ILlmProvider, Task<TResult>> call,
        CancellationToken cancellationToken)
    {
        EnsureAnyProviderConfigured();

        LlmProviderException? lastFailure = null;

        foreach (var provider in _providers)
        {
            for (var attempt = 1; attempt <= _maxAttemptsPerProvider; attempt++)
            {
                try
                {
                    return await call(provider);
                }
                catch (LlmProviderException failure) when (failure.IsTransient)
                {
                    lastFailure = failure;

                    if (attempt < _maxAttemptsPerProvider)
                    {
                        await Task.Delay(BackoffFor(attempt), cancellationToken);
                    }
                }
            }
        }

        throw lastFailure ?? AllProvidersFailed();
    }

    private TimeSpan BackoffFor(int attempt)
        => _initialBackoff * Math.Pow(2, attempt - 1);

    private void EnsureAnyProviderConfigured()
    {
        if (_providers.Count == 0)
        {
            throw new InvalidOperationException(
                "No LLM provider is configured. Set Ai:Providers:Ollama:BaseUrl (or the OpenAI-compatible provider's BaseUrl) in configuration.");
        }
    }

    private LlmProviderException AllProvidersFailed()
        => new(Name, $"All {_providers.Count} configured LLM providers failed.", isTransient: true);
}
