using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Identity;
using Orbita.Domain.Identity;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

/// <summary>
/// ORB-A16 against a real Postgres, connected as <c>orbita_app</c> so Row Level Security
/// actually applies. That is the whole point of these: the feature exists because the
/// policy on <c>memberships</c> returns zero rows without a tenant, and the fix is a second
/// policy — neither of which a test with an in-memory provider would exercise at all.
/// </summary>
public sealed class CurrentUserApiTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    [Fact]
    public async Task Signing_in_tells_you_which_organization_to_open()
    {
        // The bug this closes: on a device that has never been used there is nothing
        // remembered, the token carries no tenant, and every other route needs one.
        var client = TestRequests.CreateClient(fixture);
        var (email, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Panadería Aurora");

        var me = await GetMeAsync(client, cookies);

        Assert.Equal(email, me.Email);
        var membership = Assert.Single(me.Memberships);
        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal("Panadería Aurora", membership.Name);
        Assert.False(string.IsNullOrWhiteSpace(membership.Slug));
        Assert.Equal(MemberRole.Owner, membership.Role);
    }

    [Fact]
    public async Task You_never_see_somebody_elses_organizations()
    {
        // The policy matches on user_id. If it had been written to simply drop the tenant
        // check, this would return both.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Empresa A");
        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Empresa B");

        var meA = await GetMeAsync(client, cookiesA);
        var meB = await GetMeAsync(client, cookiesB);

        Assert.Equal(tenantA, Assert.Single(meA.Memberships).TenantId);
        Assert.Equal(tenantB, Assert.Single(meB.Memberships).TenantId);
        Assert.DoesNotContain(meA.Memberships, m => m.TenantId == tenantB);
        Assert.DoesNotContain(meB.Memberships, m => m.TenantId == tenantA);
    }

    [Fact]
    public async Task Belonging_to_two_organizations_returns_both_with_the_right_role()
    {
        // The case the picker exists for: the same person invited into somebody else's
        // organization while owning their own.
        var client = TestRequests.CreateClient(fixture);
        var (ownerEmail, ownerPassword, ownTenant, ownCookies) =
            await TestRequests.RegisterAndLogInOwnerAsync(client, "Empresa Propia");
        var (_, _, otherTenant, otherCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Empresa Ajena");

        await InviteAndAcceptAsync(client, otherCookies, otherTenant, ownerEmail, ownerPassword, MemberRole.Agent);

        // Sign in again: the session is what carries the identity, and it has not changed.
        var me = await GetMeAsync(client, ownCookies);

        Assert.Equal(2, me.Memberships.Count);
        Assert.Equal(MemberRole.Owner, me.Memberships.Single(m => m.TenantId == ownTenant).Role);
        Assert.Equal(MemberRole.Agent, me.Memberships.Single(m => m.TenantId == otherTenant).Role);
    }

    [Fact]
    public async Task An_invitation_that_was_never_accepted_is_not_an_organization()
    {
        // It is an offer, not somewhere you can act. Listing it would put a door in the
        // picker that opens onto a 403.
        var client = TestRequests.CreateClient(fixture);
        var (email, _, ownTenant, ownCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Empresa Propia");
        var (_, _, otherTenant, otherCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Empresa Ajena");

        var invited = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{otherTenant}/invitations",
            otherCookies,
            new { email, fullName = "Invitado", role = "Agent" });
        invited.EnsureSuccessStatusCode();

        var me = await GetMeAsync(client, ownCookies);

        Assert.Equal(ownTenant, Assert.Single(me.Memberships).TenantId);
    }

    [Fact]
    public async Task A_member_who_was_removed_stops_seeing_that_organization()
    {
        var client = TestRequests.CreateClient(fixture);
        var (memberEmail, memberPassword, ownTenant, memberCookies) =
            await TestRequests.RegisterAndLogInOwnerAsync(client, "Empresa Propia");
        var (_, _, otherTenant, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Empresa Ajena");

        var membershipId = await InviteAndAcceptAsync(
            client, ownerCookies, otherTenant, memberEmail, memberPassword, MemberRole.Agent);

        Assert.Equal(2, (await GetMeAsync(client, memberCookies)).Memberships.Count);

        var removed = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{otherTenant}/members/{membershipId}", ownerCookies);
        removed.EnsureSuccessStatusCode();

        Assert.Equal(ownTenant, Assert.Single((await GetMeAsync(client, memberCookies)).Memberships).TenantId);
    }

    [Fact]
    public async Task The_new_policy_does_not_widen_a_normal_tenant_scoped_read()
    {
        // The risk worth testing: `app.user_id` is set with SET LOCAL, so it must be gone
        // by the time anything else runs on that pooled connection. If it leaked, listing
        // one organization's members could start including rows from another.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Empresa A");
        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Empresa B");

        // Set app.user_id on a connection, then immediately do a tenant-scoped read.
        await GetMeAsync(client, cookiesA);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantA}/members", cookiesA);
        response.EnsureSuccessStatusCode();
        var members = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(tenantB.ToString(), members, StringComparison.OrdinalIgnoreCase);

        // And B's owner still cannot reach A at all.
        var crossTenant = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantA}/members", cookiesB);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);
    }

    [Fact]
    public async Task Without_a_session_there_is_no_answer()
    {
        var response = await TestRequests.SendAsync(
            TestRequests.CreateClient(fixture), HttpMethod.Get, "/api/auth/me", new CookieJar());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<CurrentUser> GetMeAsync(HttpClient client, CookieJar cookies)
    {
        var response = await TestRequests.SendAsync(client, HttpMethod.Get, "/api/auth/me", cookies);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CurrentUser>(TestRequests.JsonOptions))!;
    }

    /// <summary>Invites an existing account into a tenant and accepts, returning the membership id.</summary>
    private async Task<Guid> InviteAndAcceptAsync(
        HttpClient client,
        CookieJar inviterCookies,
        Guid tenantId,
        string email,
        string password,
        MemberRole role)
    {
        var invited = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/invitations",
            inviterCookies,
            new { email, fullName = "Invitado", role = role.ToString() });
        invited.EnsureSuccessStatusCode();

        // The email sender is captured by the fixture, so the one-use token is readable
        // here the same way a real inbox would carry it.
        var token = fixture.InvitationEmails.LatestTokenFor(email);

        var accepted = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            "/api/invitations/accept",
            new CookieJar(),
            new { token, password });
        accepted.EnsureSuccessStatusCode();

        var members = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/members", inviterCookies);
        members.EnsureSuccessStatusCode();

        var listed = await members.Content.ReadFromJsonAsync<List<TeamMemberProbe>>(TestRequests.JsonOptions);

        return listed!.Single(m => string.Equals(m.Email, email, StringComparison.OrdinalIgnoreCase)).MembershipId;
    }

    /// <summary>Only the two fields these tests need off the team-members listing.</summary>
    private sealed record TeamMemberProbe(Guid MembershipId, string Email);
}
