using Orbita.Application.Ai;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class ResilientLlmProviderTests
{
    private static readonly LlmCompletionRequest Request = new(
        "llama3.1",
        [LlmMessage.User("hola")],
        Temperature: 0.3m,
        MaxTokens: 800);

    private static ResilientLlmProvider Build(params ILlmProvider[] providers)
        => new(providers, maxAttemptsPerProvider: 3, initialBackoff: TimeSpan.Zero);

    [Fact]
    public async Task CompleteAsync_retries_the_same_provider_before_giving_up_on_it()
    {
        var primary = new StubLlmProvider(
            "primary",
            () => StubLlmProvider.Transient("primary"),
            () => StubLlmProvider.Transient("primary"),
            () => null);

        var result = await Build(primary).CompleteAsync(Request, CancellationToken.None);

        Assert.Equal("ok", result.Content);
        Assert.Equal(3, primary.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_fails_over_to_the_next_provider_once_the_first_is_exhausted()
    {
        var primary = new StubLlmProvider(
            "primary",
            () => StubLlmProvider.Transient("primary"),
            () => StubLlmProvider.Transient("primary"),
            () => StubLlmProvider.Transient("primary"));
        var fallback = StubLlmProvider.AlwaysSucceeds("fallback");

        var result = await Build(primary, fallback).CompleteAsync(Request, CancellationToken.None);

        Assert.Equal("ok", result.Content);
        Assert.Equal(3, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_does_not_retry_or_fail_over_on_a_permanent_failure()
    {
        // A 4xx means the request is wrong, not that the provider is down — asking
        // again, or asking someone else, returns the same answer.
        var primary = new StubLlmProvider("primary", () => StubLlmProvider.Permanent("primary"));
        var fallback = StubLlmProvider.AlwaysSucceeds("fallback");

        var failure = await Assert.ThrowsAsync<LlmProviderException>(
            () => Build(primary, fallback).CompleteAsync(Request, CancellationToken.None));

        Assert.False(failure.IsTransient);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_surfaces_the_last_transient_failure_when_every_provider_is_down()
    {
        var primary = new StubLlmProvider("primary", () => StubLlmProvider.Transient("primary"));
        var fallback = new StubLlmProvider("fallback", () => StubLlmProvider.Transient("fallback"));

        var failure = await Assert.ThrowsAsync<LlmProviderException>(
            () => Build(primary, fallback).CompleteAsync(Request, CancellationToken.None));

        Assert.True(failure.IsTransient);
        Assert.Equal(3, primary.CallCount);
        Assert.Equal(3, fallback.CallCount);
    }

    [Fact]
    public async Task Unconfigured_providers_are_skipped_entirely()
    {
        var unconfigured = new StubLlmProvider("no-credentials") { IsConfigured = false };
        var configured = StubLlmProvider.AlwaysSucceeds("configured");

        var provider = Build(unconfigured, configured);

        await provider.CompleteAsync(Request, CancellationToken.None);

        Assert.Equal(["configured"], provider.ProviderNames);
        Assert.Equal(0, unconfigured.CallCount);
    }

    [Fact]
    public async Task Calling_with_no_configured_provider_explains_what_to_configure()
    {
        var provider = Build(new StubLlmProvider("none") { IsConfigured = false });

        Assert.False(provider.IsConfigured);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CompleteAsync(Request, CancellationToken.None));

        Assert.Contains("Ai:Providers", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbedAsync_gets_the_same_retry_and_failover_treatment()
    {
        var primary = new StubLlmProvider("primary", () => StubLlmProvider.Transient("primary"));
        var fallback = StubLlmProvider.AlwaysSucceeds("fallback");

        var result = await Build(primary, fallback).EmbedAsync("hola", "nomic-embed-text", CancellationToken.None);

        Assert.Equal(2, result.Vector.Count);
        Assert.Equal("nomic-embed-text", result.Usage.Model);
    }

    [Fact]
    public async Task StreamAsync_fails_over_when_the_first_provider_dies_before_any_chunk()
    {
        var primary = new StubLlmProvider("primary", () => StubLlmProvider.Transient("primary"));
        var fallback = StubLlmProvider.AlwaysSucceeds("fallback");

        var chunks = new List<LlmChunk>();
        await foreach (var chunk in Build(primary, fallback).StreamAsync(Request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.True(chunks[^1].IsFinal);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task StreamAsync_propagates_a_failure_that_happens_after_chunks_were_handed_out()
    {
        // Restarting here would replay a second, different reply on top of text the
        // caller already received.
        var primary = new StubLlmProvider("primary", () => StubLlmProvider.Transient("primary"))
        {
            ChunksBeforeFailure = ["hola "],
        };
        var fallback = StubLlmProvider.AlwaysSucceeds("fallback");

        var received = new List<string>();

        await Assert.ThrowsAsync<LlmProviderException>(async () =>
        {
            await foreach (var chunk in Build(primary, fallback).StreamAsync(Request, CancellationToken.None))
            {
                if (chunk.DeltaContent is { } text)
                {
                    received.Add(text);
                }
            }
        });

        Assert.Equal(["hola "], received);
        Assert.Equal(0, fallback.CallCount);
    }
}
