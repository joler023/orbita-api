using System.Net;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// Answers each request with a canned response, so the LLM adapters can be exercised
/// against the exact JSON their providers document without Ollama running or any
/// account existing. Same idea as the fake payment provider the API fixture swaps in
/// for Stripe and Wompi.
/// </summary>
internal sealed class StubHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new(responses);

    public List<string> ReceivedBodies { get; } = [];

    public List<Uri> ReceivedUris { get; } = [];

    public static StubHttpMessageHandler RespondingWith(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });

    /// <summary>Server-sent events, the shape OpenAI-compatible endpoints stream in.</summary>
    public static StubHttpMessageHandler RespondingWithSse(params string[] frames)
    {
        var body = string.Concat(frames.Select(frame => $"data: {frame}\n\n"));
        return new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/event-stream") });
    }

    /// <summary>Newline-delimited JSON, the shape Ollama streams in.</summary>
    public static StubHttpMessageHandler RespondingWithNdjson(params string[] lines)
        => new(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Join('\n', lines), System.Text.Encoding.UTF8, "application/x-ndjson"),
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ReceivedUris.Add(request.RequestUri!);

        if (request.Content is not null)
        {
            ReceivedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        return _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("no scripted response left") };
    }
}
