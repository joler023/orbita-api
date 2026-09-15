using System.Net.Http.Json;
using System.Text.Json;
using Orbita.Application.Channels;

namespace Orbita.Infrastructure.Channels.Meta;

/// <summary>
/// One place that knows how Graph API reports failure: a non-2xx status whose body is
/// an <see cref="MetaErrorEnvelope"/>. Every Meta client funnels responses through
/// here so <see cref="MetaApiException"/> always carries Meta's own message and code.
/// </summary>
internal static class MetaGraphResponseReader
{
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
                ?? throw new MetaApiException("Graph API returned an empty response.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MetaApiException(DescribeFailure(response, body), ReadErrorCode(body));
    }

    private static string DescribeFailure(HttpResponseMessage response, string body)
    {
        var error = TryReadError(body);
        return error?.Message is { Length: > 0 } message
            ? $"{message} (HTTP {(int)response.StatusCode}, code {error.Code?.ToString() ?? "n/a"})"
            : $"Graph API call failed with HTTP {(int)response.StatusCode}.";
    }

    private static int? ReadErrorCode(string body) => TryReadError(body)?.Code;

    private static MetaError? TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MetaErrorEnvelope>(body)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
