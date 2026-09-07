using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Orbita.Application.Identity;

namespace Orbita.IntegrationTests.TestSupport;

/// <summary>Shared helpers for driving the real HTTP pipeline against a cookie jar.</summary>
public static class TestRequests
{
    /// <summary>
    /// Matches Program.cs's controller JSON options: enums like MemberRole travel as
    /// their names ("Agent"), not raw integers. HttpClient's ReadFromJsonAsync uses
    /// plain System.Text.Json defaults otherwise, which cannot parse them back.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static HttpClient CreateClient(TenantsApiFixture fixture)
        => fixture.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    public static async Task<(string Email, string Password)> RegisterAsync(HttpClient client, string businessName = "Acme Corp")
    {
        const string password = "correct-horse-battery";
        var email = $"{Guid.NewGuid():N}@acme.com";
        var response = await client.PostAsJsonAsync(
            "/api/organizations",
            new RegisterOrganizationRequest(businessName, "Jane Doe", email, password));
        response.EnsureSuccessStatusCode();
        return (email, password);
    }

    /// <summary>Registers a fresh organization and logs its owner in, ready to drive tenant-scoped endpoints.</summary>
    public static async Task<(string Email, string Password, Guid TenantId, CookieJar Cookies)> RegisterAndLogInOwnerAsync(
        HttpClient client,
        string businessName = "Acme Corp")
    {
        const string password = "correct-horse-battery";
        var email = $"{Guid.NewGuid():N}@acme.com";
        var registerResponse = await client.PostAsJsonAsync(
            "/api/organizations",
            new RegisterOrganizationRequest(businessName, "Jane Doe", email, password));
        registerResponse.EnsureSuccessStatusCode();
        var registered = await registerResponse.Content.ReadFromJsonAsync<RegisterOrganizationResult>();

        var cookies = new CookieJar();
        var loginResponse = await SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();

        return (email, password, registered!.TenantId, cookies);
    }

    public static async Task<HttpResponseMessage> SendAsync(
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
}
