using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Orbita.Api.Controllers;
using Orbita.Application.Identity;

namespace Orbita.IntegrationTests;

/// <summary>
/// Manages cookies explicitly (HandleCookies: false on the client) instead of relying
/// on HttpClient's built-in cookie jar, so a test can hold on to an old, already
/// rotated-away refresh token value on purpose and replay it — exactly the reuse
/// scenario ORB-A06's acceptance criteria describe.
/// </summary>
public sealed class AuthControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public AuthControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Login_WithValidCredentials_SetsCookiesAndAuthenticatesFollowUpRequests()
    {
        var client = CreateClient();
        var (email, password) = await RegisterAsync(client);
        var cookies = new CookieJar();

        var loginResponse = await SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var body = await loginResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.Equal(email, body!.Email);

        var meResponse = await SendAsync(client, HttpMethod.Get, "/api/auth/me", cookies);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<CurrentUserIdResponse>();
        Assert.Equal(body.UserId, me!.UserId);
    }

    [Fact]
    public async Task Me_WithoutLoggingIn_ReturnsUnauthorized()
    {
        var response = await SendAsync(CreateClient(), HttpMethod.Get, "/api/auth/me", new CookieJar());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var (email, _) = await RegisterAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, "totally-wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_AfterMaxFailedAttempts_LocksTheAccountEvenWithTheRightPassword()
    {
        var client = CreateClient();
        var (email, password) = await RegisterAsync(client);

        for (var i = 0; i < AuthenticationService.MaxFailedLoginAttempts; i++)
        {
            await SendAsync(client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, "wrong-password"));
        }

        var response = await SendAsync(client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTheCookieAndTheOldRefreshTokenCanNoLongerBeUsed()
    {
        var client = CreateClient();
        var (email, password) = await RegisterAsync(client);
        var cookies = new CookieJar();
        await SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));
        var staleCookies = cookies.Clone();

        var refreshResponse = await SendAsync(client, HttpMethod.Post, "/api/auth/refresh", cookies);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.NotEqual(staleCookies["refresh_token"], cookies["refresh_token"]);

        // Replay the token that was just rotated away — must fail, and per ORB-A06's
        // reuse-detection requirement, must also burn the token the client currently
        // holds, since a replay means the whole family may have been stolen.
        var replayResponse = await SendAsync(client, HttpMethod.Post, "/api/auth/refresh", staleCookies);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        var currentRefreshResponse = await SendAsync(client, HttpMethod.Post, "/api/auth/refresh", cookies);
        Assert.Equal(HttpStatusCode.Unauthorized, currentRefreshResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutACookie_ReturnsUnauthorized()
    {
        var response = await SendAsync(CreateClient(), HttpMethod.Post, "/api/auth/refresh", new CookieJar());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_InvalidatesTheRefreshTokenServerSide()
    {
        var client = CreateClient();
        var (email, password) = await RegisterAsync(client);
        var cookies = new CookieJar();
        await SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));

        var logoutResponse = await SendAsync(client, HttpMethod.Post, "/api/auth/logout", cookies);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await SendAsync(client, HttpMethod.Post, "/api/auth/refresh", cookies);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    private HttpClient CreateClient() => _fixture.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<(string Email, string Password)> RegisterAsync(HttpClient client)
    {
        const string password = "correct-horse-battery";
        var email = $"{Guid.NewGuid():N}@acme.com";
        var response = await client.PostAsJsonAsync(
            "/api/organizations",
            new RegisterOrganizationRequest("Acme Corp", "Jane Doe", email, password));
        response.EnsureSuccessStatusCode();
        return (email, password);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        CookieJar cookies,
        object? jsonBody = null)
    {
        using var request = new HttpRequestMessage(method, url);
        var cookieHeader = cookies.ToHeader();
        if (cookieHeader.Length > 0)
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody);
        }

        var response = await client.SendAsync(request);
        cookies.Capture(response);
        return response;
    }

    private sealed class CookieJar
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
}
