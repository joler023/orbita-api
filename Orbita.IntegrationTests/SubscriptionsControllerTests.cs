using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Billing;
using Orbita.Application.Identity;
using Orbita.Domain.Billing;
using Orbita.Domain.Identity;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

public sealed class SubscriptionsControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public SubscriptionsControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Subscribe_ThenGet_ReturnsTheNewSubscription()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var planId = await GetStarterPlanIdAsync(client);

        var subscribeResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/subscription", ownerCookies, new SubscribeRequest(planId, "tok_visa"));
        Assert.Equal(HttpStatusCode.Created, subscribeResponse.StatusCode);
        var created = await subscribeResponse.Content.ReadFromJsonAsync<SubscriptionDto>(TestRequests.JsonOptions);
        Assert.Equal(planId, created!.PlanId);

        var getResponse = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/subscription", ownerCookies);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<SubscriptionDto>(TestRequests.JsonOptions);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task Subscribe_ByAnAgent_ReturnsForbidden()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var agentEmail = $"{Guid.NewGuid():N}@acme.com";
        await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations", ownerCookies, new InviteTeamMemberRequest(agentEmail, MemberRole.Agent));
        var agentToken = _fixture.InvitationEmails.LatestTokenFor(agentEmail);
        var agentCookies = new CookieJar();
        await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/invitations/accept", agentCookies, new AcceptInvitationRequest(agentToken, "Agent Person", "agent-password-123"));
        var planId = await GetStarterPlanIdAsync(client);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/subscription", agentCookies, new SubscribeRequest(planId, "tok_visa"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Subscribe_ByAnAdmin_ReturnsForbidden()
    {
        // ManageBilling is Owner-only, unlike ManageTeam/ManageSettings — an Admin
        // should not be able to touch billing.
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var adminEmail = $"{Guid.NewGuid():N}@acme.com";
        await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/invitations", ownerCookies, new InviteTeamMemberRequest(adminEmail, MemberRole.Admin));
        var adminToken = _fixture.InvitationEmails.LatestTokenFor(adminEmail);
        var adminCookies = new CookieJar();
        await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/invitations/accept", adminCookies, new AcceptInvitationRequest(adminToken, "Admin Person", "admin-password-123"));
        var planId = await GetStarterPlanIdAsync(client);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/subscription", adminCookies, new SubscribeRequest(planId, "tok_visa"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Subscribe_Twice_ReturnsConflict()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var planId = await GetStarterPlanIdAsync(client);
        await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/subscription", ownerCookies, new SubscribeRequest(planId, "tok_visa"));

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, $"/api/tenants/{tenantId}/subscription", ownerCookies, new SubscribeRequest(planId, "tok_visa"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ChangePlan_UpdatesTheSubscribedPlan()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var starterPlanId = await GetStarterPlanIdAsync(client);
        var growthPlanId = await GetPlanIdAsync(client, "growth");
        await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/subscription", ownerCookies, new SubscribeRequest(starterPlanId, "tok_visa"));

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Patch, $"/api/tenants/{tenantId}/subscription", ownerCookies, new ChangePlanRequest(growthPlanId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<SubscriptionDto>(TestRequests.JsonOptions);
        Assert.Equal(growthPlanId, updated!.PlanId);
    }

    [Fact]
    public async Task Cancel_MarksTheSubscriptionCanceled()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var planId = await GetStarterPlanIdAsync(client);
        await TestRequests.SendAsync(client, HttpMethod.Post, $"/api/tenants/{tenantId}/subscription", ownerCookies, new SubscribeRequest(planId, "tok_visa"));

        var cancelResponse = await TestRequests.SendAsync(client, HttpMethod.Delete, $"/api/tenants/{tenantId}/subscription", ownerCookies);
        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

        var getResponse = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/subscription", ownerCookies);
        var subscription = await getResponse.Content.ReadFromJsonAsync<SubscriptionDto>(TestRequests.JsonOptions);
        Assert.Equal(SubscriptionStatus.Canceled, subscription!.Status);
    }

    [Fact]
    public async Task ListInvoices_WithoutASubscription_ReturnsNotFound()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/subscription/invoices", ownerCookies);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> GetStarterPlanIdAsync(HttpClient client) => await GetPlanIdAsync(client, "starter");

    private static async Task<Guid> GetPlanIdAsync(HttpClient client, string code)
    {
        var plans = await client.GetFromJsonAsync<List<PlanDto>>("/api/plans");
        return plans!.Single(p => p.Code == code).Id;
    }
}
