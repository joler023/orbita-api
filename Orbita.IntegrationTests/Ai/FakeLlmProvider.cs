using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// A deterministic stand-in for a real model, swapped in by <c>TenantsApiFixture</c> the
/// same way <c>FakePaymentProvider</c> stands in for Stripe and Wompi. Without it these
/// tests would need Ollama installed or an OpenRouter balance, and would be flaky either
/// way.
///
/// Embeddings are derived from a hash of the text, so the same text always produces the
/// same vector and different texts produce different ones — enough for ORB-C02's "did it
/// index?" and, later, for ORB-C03 to assert that the closest match is the right chunk.
/// </summary>
public sealed class FakeLlmProvider : ILlmProvider
{
    public string Name => "fake";

    public bool IsConfigured => true;

    /// <summary>Set to make the next call fail the way an unreachable provider does.</summary>
    public LlmProviderException? NextFailure { get; set; }

    public int EmbedCallCount { get; private set; }

    /// <summary>
    /// What the last completion was asked with. ORB-C11's tests assert on the prompt — that
    /// the draft's instructions and the retrieved passages actually reached the model — and
    /// the reply alone cannot show that.
    /// </summary>
    public LlmCompletionRequest? LastCompletionRequest { get; private set; }

    /// <summary>Set to control what the next completion answers.</summary>
    public string NextReply { get; set; } = "respuesta de prueba";

    public Task<LlmCompletionResult> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken)
    {
        LastCompletionRequest = request;
        ThrowIfScripted();

        return Task.FromResult(new LlmCompletionResult(NextReply, [], Usage("fake-chat")));
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfScripted();
        yield return new LlmChunk("respuesta de prueba", null, IsFinal: false, Usage: null);
        yield return new LlmChunk(null, null, IsFinal: true, Usage("fake-chat"));
        await Task.CompletedTask;
    }

    public Task<LlmEmbeddingResult> EmbedAsync(string text, Guid tenantId, CancellationToken cancellationToken)
    {
        EmbedCallCount++;
        ThrowIfScripted();

        return Task.FromResult(new LlmEmbeddingResult(Embed(text), Usage("fake-embed")));
    }

    /// <summary>
    /// A unit-length vector seeded from the text's SHA-256, so it is stable across runs
    /// and across processes — a random vector would make any relevance assertion
    /// meaningless.
    /// </summary>
    public static float[] Embed(string text)
    {
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var random = new Random(BitConverter.ToInt32(seed, 0));
        var vector = new float[KnowledgeChunk.EmbeddingDimensions];

        double sumOfSquares = 0;

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)((random.NextDouble() * 2) - 1);
            sumOfSquares += vector[i] * vector[i];
        }

        var magnitude = (float)Math.Sqrt(sumOfSquares);

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= magnitude;
        }

        return vector;
    }

    private void ThrowIfScripted()
    {
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }
    }

    private static LlmUsage Usage(string model) => new(model, 10, 5, 0m, 1, "stop");

    /// <summary>Between tests that share the fixture, so one does not read another's prompt.</summary>
    public void Reset()
    {
        LastCompletionRequest = null;
        NextReply = "respuesta de prueba";
        NextFailure = null;
    }
}
