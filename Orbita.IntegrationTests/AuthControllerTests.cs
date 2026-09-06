using System.Net;
using System.Net.Http.Json;
using Orbita.Api.Controllers;
using Orbita.Application.Identity;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

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
        var client = TestRequests.CreateClient(_fixture);
        var (email, password) = await TestRequests.RegisterAsync(client);
        var cookies = new CookieJar();

        var loginResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var body = await loginResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.Equal(email, body!.Email);

        var meResponse = await TestRequests.SendAsync(client, HttpMethod.Get, "/api/auth/me", cookies);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<CurrentUserIdResponse>();
        Assert.Equal(body.UserId, me!.UserId);
    }

    [Fact]
    public async Task Me_WithoutLoggingIn_ReturnsUnauthorized()
    {
        var response = await TestRequests.SendAsync(TestRequests.CreateClient(_fixture), HttpMethod.Get, "/api/auth/me", new CookieJar());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, _) = await TestRequests.RegisterAsync(client);

        var response = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, "totally-wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_AfterMaxFailedAttempts_LocksTheAccountEvenWithTheRightPassword()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, password) = await TestRequests.RegisterAsync(client);

        for (var i = 0; i < AuthenticationService.MaxFailedLoginAttempts; i++)
        {
            await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, "wrong-password"));
        }

        var response = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTheCookieAndTheOldRefreshTokenCanNoLongerBeUsed()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, password) = await TestRequests.RegisterAsync(client);
        var cookies = new CookieJar();
        await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));
        var staleCookies = cookies.Clone();

        var refreshResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/refresh", cookies);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.NotEqual(staleCookies["refresh_token"], cookies["refresh_token"]);

        // Replay the token that was just rotated away — must fail, and per ORB-A06's
        // reuse-detection requirement, must also burn the token the client currently
        // holds, since a replay means the whole family may have been stolen.
        var replayResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/refresh", staleCookies);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        var currentRefreshResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/refresh", cookies);
        Assert.Equal(HttpStatusCode.Unauthorized, currentRefreshResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutACookie_ReturnsUnauthorized()
    {
        var response = await TestRequests.SendAsync(TestRequests.CreateClient(_fixture), HttpMethod.Post, "/api/auth/refresh", new CookieJar());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_InvalidatesTheRefreshTokenServerSide()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, password) = await TestRequests.RegisterAsync(client);
        var cookies = new CookieJar();
        await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));

        var logoutResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/logout", cookies);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/refresh", cookies);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }
}
