using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Identity;
using Orbita.Domain.Identity;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

public sealed class TeamMembersControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public TeamMembersControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task List_ReturnsTheOwnerAndAcceptedMembers()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (ownerEmail, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        await InviteAndAcceptAsync(client, tenantId, ownerCookies, MemberRole.Agent);

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/members", ownerCookies);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var members = await response.Content.ReadFromJsonAsync<List<TeamMemberSummary>>(TestRequests.JsonOptions);
        Assert.Equal(2, members!.Count);
        Assert.Contains(members, m => m.Email == ownerEmail && m.Role == MemberRole.Owner);
        Assert.Contains(members, m => m.Role == MemberRole.Agent);
    }

    [Fact]
    public async Task List_ByAnAuthenticatedOutsiderWithNoMembershipInThatTenant_ReturnsForbidden()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, _) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Acme Corp");
        var (_, _, _, outsiderCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Other Business");

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/members", outsiderCookies);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ChangeRole_ByAnAgent_ReturnsForbidden()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (agentMembershipId, agentCookies) = await InviteAndAcceptAsync(client, tenantId, ownerCookies, MemberRole.Agent);

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantId}/members/{agentMembershipId}/role",
            agentCookies,
            new ChangeMemberRoleRequest(MemberRole.Admin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ChangeRole_PromotingAnAgentToAdmin_Succeeds()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (agentMembershipId, _) = await InviteAndAcceptAsync(client, tenantId, ownerCookies, MemberRole.Agent);

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantId}/members/{agentMembershipId}/role",
            ownerCookies,
            new ChangeMemberRoleRequest(MemberRole.Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<TeamMemberSummary>(TestRequests.JsonOptions);
        Assert.Equal(MemberRole.Admin, summary!.Role);
    }

    [Fact]
    public async Task ChangeRole_DemotingTheOnlyOwner_ReturnsConflict()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var listResponse = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/members", ownerCookies);
        var members = await listResponse.Content.ReadFromJsonAsync<List<TeamMemberSummary>>(TestRequests.JsonOptions);
        var ownerMembershipId = members!.Single().MembershipId;

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantId}/members/{ownerMembershipId}/role",
            ownerCookies,
            new ChangeMemberRoleRequest(MemberRole.Admin));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ChangeRole_WithAnUnknownMembershipId_ReturnsNotFound()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/tenants/{tenantId}/members/{Guid.NewGuid()}/role",
            ownerCookies,
            new ChangeMemberRoleRequest(MemberRole.Admin));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Remove_AnAgent_TakesThemOutOfTheActiveMemberList()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (agentMembershipId, _) = await InviteAndAcceptAsync(client, tenantId, ownerCookies, MemberRole.Agent);

        var removeResponse = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/members/{agentMembershipId}", ownerCookies);
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var listResponse = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/members", ownerCookies);
        var members = await listResponse.Content.ReadFromJsonAsync<List<TeamMemberSummary>>(TestRequests.JsonOptions);
        Assert.DoesNotContain(members!, m => m.MembershipId == agentMembershipId);
    }

    [Fact]
    public async Task Remove_TheOnlyOwner_ReturnsConflict()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var listResponse = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/members", ownerCookies);
        var members = await listResponse.Content.ReadFromJsonAsync<List<TeamMemberSummary>>(TestRequests.JsonOptions);
        var ownerMembershipId = members!.Single().MembershipId;

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/members/{ownerMembershipId}", ownerCookies);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Invites a new person with the given role and accepts on their behalf, returning their membership id and a cookie jar logged in as them.</summary>
    private async Task<(Guid MembershipId, CookieJar Cookies)> InviteAndAcceptAsync(
        HttpClient client,
        Guid tenantId,
        CookieJar ownerCookies,
        MemberRole role)
    {
        var email = $"{Guid.NewGuid():N}@acme.com";
        var inviteResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations", ownerCookies, new InviteTeamMemberRequest(email, role));
        var summary = await inviteResponse.Content.ReadFromJsonAsync<TeamInvitationSummary>(TestRequests.JsonOptions);
        var rawToken = _fixture.InvitationEmails.LatestTokenFor(email);

        var memberCookies = new CookieJar();
        await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/invitations/accept", memberCookies, new AcceptInvitationRequest(rawToken, "Member Person", "member-password-123"));

        return (summary!.MembershipId, memberCookies);
    }
}
