namespace Orbita.IntegrationTests.TestSupport;

/// <summary>
/// Manages cookies explicitly instead of relying on HttpClient's built-in cookie jar
/// (tests create their client with HandleCookies: false), so a test can hold on to an
/// old, already-rotated-away cookie value on purpose and replay it later — the reuse
/// scenario ORB-A06's acceptance criteria describe.
/// </summary>
public sealed class CookieJar
{
    private readonly Dictionary<string, string> _values;

    public CookieJar() => _values = new Dictionary<string, string>();

    private CookieJar(Dictionary<string, string> values) => _values = values;

    public string this[string name] => _values[name];

    public CookieJar Clone() => new(new Dictionary<string, string>(_values));

    public void Capture(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return;
        }

        foreach (var setCookie in setCookies)
        {
            var nameAndValue = setCookie.Split(';', 2)[0];
            var separatorIndex = nameAndValue.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            _values[nameAndValue[..separatorIndex]] = nameAndValue[(separatorIndex + 1)..];
        }
    }

    public string ToHeader() => string.Join("; ", _values.Select(kv => $"{kv.Key}={kv.Value}"));
}
