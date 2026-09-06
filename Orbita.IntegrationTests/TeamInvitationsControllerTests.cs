using System.Net;
using System.Net.Http.Json;
using Orbita.Api.Controllers;
using Orbita.Application.Identity;
using Orbita.Domain.Identity;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

public sealed class TeamInvitationsControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public TeamInvitationsControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Invite_ThenAccept_LetsTheNewPersonLogInAfterward()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await RegisterAndLogInOwnerAsync(client);
        var inviteeEmail = $"{Guid.NewGuid():N}@acme.com";

        var inviteResponse = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/invitations",
            ownerCookies,
            new InviteTeamMemberRequest(inviteeEmail, MemberRole.Agent));

        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);
        var summary = await inviteResponse.Content.ReadFromJsonAsync<TeamInvitationSummary>(TestRequests.JsonOptions);
        Assert.Equal(inviteeEmail, summary!.Email);

        var rawToken = _fixture.InvitationEmails.LatestTokenFor(inviteeEmail);
        var acceptResponse = await client.PostAsJsonAsync(
            "/api/invitations/accept",
            new AcceptInvitationRequest(rawToken, "Invitee Name", "a-brand-new-password"));

        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        var accepted = await acceptResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.Equal(inviteeEmail, accepted!.Email);
        Assert.Equal("Invitee Name", accepted.FullName);

        // Accepting logs them in already, but the point of setting a real password is
        // that they can also log in again later on their own.
        var loginResponse = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            "/api/auth/login",
            new CookieJar(),
            new LoginRequest(inviteeEmail, "a-brand-new-password"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Invite_ByANonAdminMember_ReturnsForbidden()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await RegisterAndLogInOwnerAsync(client);

        // Invite a plain Agent, accept as them, then try to invite someone else as that Agent.
        var agentEmail = $"{Guid.NewGuid():N}@acme.com";
        await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations", ownerCookies, new InviteTeamMemberRequest(agentEmail, MemberRole.Agent));
        var agentToken = _fixture.InvitationEmails.LatestTokenFor(agentEmail);
        var agentCookies = new CookieJar();
        await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/invitations/accept", agentCookies, new AcceptInvitationRequest(agentToken, "Agent Person", "agent-password-123"));

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/invitations",
            agentCookies,
            new InviteTeamMemberRequest($"{Guid.NewGuid():N}@acme.com", MemberRole.Agent));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invite_SameEmailTwice_ReturnsConflict()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await RegisterAndLogInOwnerAsync(client);
        var inviteeEmail = $"{Guid.NewGuid():N}@acme.com";
        await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations", ownerCookies, new InviteTeamMemberRequest(inviteeEmail, MemberRole.Agent));

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations", ownerCookies, new InviteTeamMemberRequest(inviteeEmail, MemberRole.Admin));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Resend_IssuesANewTokenAndInvalidatesTheOldOne()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await RegisterAndLogInOwnerAsync(client);
        var inviteeEmail = $"{Guid.NewGuid():N}@acme.com";
        var inviteResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations", ownerCookies, new InviteTeamMemberRequest(inviteeEmail, MemberRole.Agent));
        var summary = await inviteResponse.Content.ReadFromJsonAsync<TeamInvitationSummary>(TestRequests.JsonOptions);
        var firstToken = _fixture.InvitationEmails.LatestTokenFor(inviteeEmail);

        var resendResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations/{summary!.MembershipId}/resend", ownerCookies);
        Assert.Equal(HttpStatusCode.OK, resendResponse.StatusCode);
        var secondToken = _fixture.InvitationEmails.LatestTokenFor(inviteeEmail);
        Assert.NotEqual(firstToken, secondToken);

        var acceptWithOldToken = await client.PostAsJsonAsync(
            "/api/invitations/accept",
            new AcceptInvitationRequest(firstToken, "Invitee", "some-password-123"));
        Assert.Equal(HttpStatusCode.BadRequest, acceptWithOldToken.StatusCode);

        var acceptWithNewToken = await client.PostAsJsonAsync(
            "/api/invitations/accept",
            new AcceptInvitationRequest(secondToken, "Invitee", "some-password-123"));
        Assert.Equal(HttpStatusCode.OK, acceptWithNewToken.StatusCode);
    }

    [Fact]
    public async Task Revoke_MakesTheInvitationUnacceptable()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await RegisterAndLogInOwnerAsync(client);
        var inviteeEmail = $"{Guid.NewGuid():N}@acme.com";
        var inviteResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations", ownerCookies, new InviteTeamMemberRequest(inviteeEmail, MemberRole.Agent));
        var summary = await inviteResponse.Content.ReadFromJsonAsync<TeamInvitationSummary>(TestRequests.JsonOptions);
        var rawToken = _fixture.InvitationEmails.LatestTokenFor(inviteeEmail);

        var revokeResponse = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/invitations/{summary!.MembershipId}", ownerCookies);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        var acceptResponse = await client.PostAsJsonAsync(
            "/api/invitations/accept",
            new AcceptInvitationRequest(rawToken, "Invitee", "some-password-123"));
        Assert.Equal(HttpStatusCode.BadRequest, acceptResponse.StatusCode);
    }

    [Fact]
    public async Task Accept_WithAnUnknownToken_ReturnsBadRequest()
    {
        var client = TestRequests.CreateClient(_fixture);

        var response = await client.PostAsJsonAsync(
            "/api/invitations/accept",
            new AcceptInvitationRequest("not-a-real-token", "Someone", "some-password-123"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<(string Email, string Password, Guid TenantId, CookieJar Cookies)> RegisterAndLogInOwnerAsync(HttpClient client)
    {
        const string password = "correct-horse-battery";
        var email = $"{Guid.NewGuid():N}@acme.com";
        var registerResponse = await client.PostAsJsonAsync(
            "/api/organizations",
            new RegisterOrganizationRequest("Acme Corp", "Jane Doe", email, password));
        registerResponse.EnsureSuccessStatusCode();
        var registered = await registerResponse.Content.ReadFromJsonAsync<RegisterOrganizationResult>();

        var cookies = new CookieJar();
        var loginResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();

        return (email, password, registered!.TenantId, cookies);
    }
}
