using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Identity;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

public sealed class PasswordResetTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public PasswordResetTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ForgotPassword_ForARegisteredEmail_SendsAResetLinkThatLogsIntoTheNewPassword()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, _) = await TestRequests.RegisterAsync(client);

        var forgotResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new RequestPasswordResetRequest(email));
        Assert.Equal(HttpStatusCode.Accepted, forgotResponse.StatusCode);

        var rawToken = _fixture.PasswordResetEmails.LatestTokenFor(email);
        var resetResponse = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(rawToken, "a-brand-new-password"));
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);

        var loginResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, "a-brand-new-password"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_ForAnUnknownEmail_StillReturnsAccepted()
    {
        var client = TestRequests.CreateClient(_fixture);

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new RequestPasswordResetRequest("nobody@acme.com"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_InvalidatesTheOldPasswordAndAllActiveSessions()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, password) = await TestRequests.RegisterAsync(client);
        var sessionCookies = new CookieJar();
        await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", sessionCookies, new LoginRequest(email, password));

        await client.PostAsJsonAsync("/api/auth/forgot-password", new RequestPasswordResetRequest(email));
        var rawToken = _fixture.PasswordResetEmails.LatestTokenFor(email);
        await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(rawToken, "a-brand-new-password"));

        // The refresh token from before the reset must no longer work.
        var refreshResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/refresh", sessionCookies);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);

        // Nor does the old password still work.
        var oldLoginResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLoginResponse.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithAnAlreadyUsedToken_ReturnsBadRequest()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, _) = await TestRequests.RegisterAsync(client);
        await client.PostAsJsonAsync("/api/auth/forgot-password", new RequestPasswordResetRequest(email));
        var rawToken = _fixture.PasswordResetEmails.LatestTokenFor(email);
        await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(rawToken, "first-new-password"));

        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(rawToken, "second-new-password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithAnUnknownToken_ReturnsBadRequest()
    {
        var client = TestRequests.CreateClient(_fixture);

        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest("not-a-real-token", "some-new-password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_RequestedTwice_InvalidatesTheFirstLink()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, _) = await TestRequests.RegisterAsync(client);

        await client.PostAsJsonAsync("/api/auth/forgot-password", new RequestPasswordResetRequest(email));
        var firstToken = _fixture.PasswordResetEmails.LatestTokenFor(email);
        await client.PostAsJsonAsync("/api/auth/forgot-password", new RequestPasswordResetRequest(email));
        var secondToken = _fixture.PasswordResetEmails.LatestTokenFor(email);
        Assert.NotEqual(firstToken, secondToken);

        var response = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(firstToken, "some-new-password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
