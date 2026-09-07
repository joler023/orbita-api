using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Audit;
using Orbita.Application.Identity;
using Orbita.Domain.Identity;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

public sealed class AuditLogControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public AuditLogControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ChangingAMembersRole_AppearsInTheAuditLog()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (agentMembershipId, _) = await InviteAndAcceptAgentAsync(client, tenantId, ownerCookies);

        await TestRequests.SendAsync(
            client, HttpMethod.Patch, $"/api/tenants/{tenantId}/members/{agentMembershipId}/role", ownerCookies, new ChangeMemberRoleRequest(MemberRole.Admin));

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/audit-log", ownerCookies);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<List<AuditLogEntryDto>>(TestRequests.JsonOptions);
        var entry = Assert.Single(entries!, e => e.Action == "membership.role_changed");
        Assert.Equal(agentMembershipId, entry.EntityId);
        Assert.Equal("Membership", entry.EntityType);
        Assert.Contains("Admin", entry.DiffJson);
    }

    [Fact]
    public async Task RemovingAMember_AppearsInTheAuditLog()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (agentMembershipId, _) = await InviteAndAcceptAgentAsync(client, tenantId, ownerCookies);

        await TestRequests.SendAsync(client, HttpMethod.Delete, $"/api/tenants/{tenantId}/members/{agentMembershipId}", ownerCookies);

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/audit-log?entityType=Membership", ownerCookies);
        var entries = await response.Content.ReadFromJsonAsync<List<AuditLogEntryDto>>(TestRequests.JsonOptions);

        Assert.Contains(entries!, e => e.Action == "membership.removed" && e.EntityId == agentMembershipId);
    }

    [Fact]
    public async Task Query_ByAnAgent_ReturnsForbidden()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (_, agentCookies) = await InviteAndAcceptAgentAsync(client, tenantId, ownerCookies);

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/audit-log", agentCookies);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Query_NeverReturnsAnotherTenantsEntries()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantAId, ownerACookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Acme Corp");
        var (agentMembershipId, _) = await InviteAndAcceptAgentAsync(client, tenantAId, ownerACookies);
        await TestRequests.SendAsync(
            client, HttpMethod.Patch, $"/api/tenants/{tenantAId}/members/{agentMembershipId}/role", ownerACookies, new ChangeMemberRoleRequest(MemberRole.Admin));

        var (_, _, tenantBId, ownerBCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Other Business");

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantBId}/audit-log", ownerBCookies);
        var entries = await response.Content.ReadFromJsonAsync<List<AuditLogEntryDto>>(TestRequests.JsonOptions);

        Assert.DoesNotContain(entries!, e => e.Action == "membership.role_changed");
    }

    private async Task<(Guid MembershipId, CookieJar Cookies)> InviteAndAcceptAgentAsync(HttpClient client, Guid tenantId, CookieJar ownerCookies)
    {
        var email = $"{Guid.NewGuid():N}@acme.com";
        var inviteResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations", ownerCookies, new InviteTeamMemberRequest(email, MemberRole.Agent));
        var summary = await inviteResponse.Content.ReadFromJsonAsync<TeamInvitationSummary>(TestRequests.JsonOptions);
        var rawToken = _fixture.InvitationEmails.LatestTokenFor(email);

        var cookies = new CookieJar();
        await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/invitations/accept", cookies, new AcceptInvitationRequest(rawToken, "Agent Person", "agent-password-123"));

        return (summary!.MembershipId, cookies);
    }
}
