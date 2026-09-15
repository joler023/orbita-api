using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Orbita.Application.Channels;
using Orbita.Application.Identity;
using Orbita.Domain.Identity;

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

    /// <summary>
    /// Invites a new person with the given role and accepts on their behalf, returning
    /// their membership id and a cookie jar logged in as them — the standard way to get
    /// a lower-privileged actor for a 403 assertion.
    /// </summary>
    public static async Task<(Guid MembershipId, CookieJar Cookies)> InviteAndAcceptAsync(
        TenantsApiFixture fixture,
        HttpClient client,
        Guid tenantId,
        CookieJar ownerCookies,
        MemberRole role)
    {
        var email = $"{Guid.NewGuid():N}@acme.com";
        var inviteResponse = await SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations", ownerCookies, new InviteTeamMemberRequest(email, role));
        inviteResponse.EnsureSuccessStatusCode();
        var summary = await inviteResponse.Content.ReadFromJsonAsync<TeamInvitationSummary>(JsonOptions);
        var rawToken = fixture.InvitationEmails.LatestTokenFor(email);

        var memberCookies = new CookieJar();
        var acceptResponse = await SendAsync(
            client, HttpMethod.Post, "/api/invitations/accept", memberCookies, new AcceptInvitationRequest(rawToken, "Member Person", "member-password-123"));
        acceptResponse.EnsureSuccessStatusCode();

        return (summary!.MembershipId, memberCookies);
    }

    /// <summary>
    /// Connects a WhatsApp account <b>and completes Meta's verification handshake</b>, so
    /// the account ends up <c>Connected</c> and can actually send.
    ///
    /// The second half is the part that is easy to forget and impossible to notice:
    /// ORB-B01 leaves a freshly connected account in <c>PendingVerification</c> until
    /// Meta calls the per-account callback back. In production Meta does that inside
    /// SubscribeWebhookAsync; here the fake client only records the subscription, so the
    /// test has to play Meta's side. A test that skips it gets an account that looks
    /// connected in the response body and refuses every send with
    /// <c>ChannelNotConnectedException</c> — which surfaces as a 409 that reads like a
    /// closed service window and has nothing to do with one.
    /// </summary>
    public static async Task<ChannelAccountDto> ConnectVerifiedWhatsAppAsync(
        TenantsApiFixture fixture,
        HttpClient client,
        Guid tenantId,
        CookieJar cookies)
    {
        var wabaId = $"waba-{Guid.NewGuid():N}";
        var request = new ConnectWhatsAppRequest($"code-{Guid.NewGuid():N}", wabaId, $"phone-{Guid.NewGuid():N}");

        var response = await SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/channels/whatsapp", cookies, request);
        response.EnsureSuccessStatusCode();
        var account = (await response.Content.ReadFromJsonAsync<ChannelAccountDto>(JsonOptions))!;

        var subscription = fixture.WhatsAppApi.LatestSubscriptionFor(wabaId);
        var verify = await client.GetAsync(
            $"/api/webhooks/whatsapp/{account.Id}?hub.mode=subscribe&hub.verify_token={subscription.VerifyToken}&hub.challenge=ok");
        verify.EnsureSuccessStatusCode();

        var reread = await SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/channels/{account.Id}", cookies);
        reread.EnsureSuccessStatusCode();

        return (await reread.Content.ReadFromJsonAsync<ChannelAccountDto>(JsonOptions))!;
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
