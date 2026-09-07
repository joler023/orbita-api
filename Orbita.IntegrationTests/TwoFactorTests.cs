using System.Net;
using System.Net.Http.Json;
using OtpNet;
using Orbita.Application.Identity;
using Orbita.Application.Tenants;
using Orbita.Domain.Identity;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

public sealed class TwoFactorTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public TwoFactorTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Setup_ThenConfirmWithAValidCode_EnablesTwoFactorAndReturnsBackupCodes()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, password) = await TestRequests.RegisterAsync(client);
        var cookies = new CookieJar();
        await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));

        var setupResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/2fa/setup", cookies);
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var setup = await setupResponse.Content.ReadFromJsonAsync<TwoFactorSetupResult>(TestRequests.JsonOptions);

        var confirmResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/2fa/confirm", cookies, new ConfirmTwoFactorSetupRequest(ComputeCode(setup!.Secret)));

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var backupCodes = await confirmResponse.Content.ReadFromJsonAsync<List<string>>();
        Assert.Equal(TwoFactorService.BackupCodeCount, backupCodes!.Count);
    }

    [Fact]
    public async Task Confirm_WithAnInvalidCode_ReturnsBadRequest()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, password) = await TestRequests.RegisterAsync(client);
        var cookies = new CookieJar();
        await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));
        await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/2fa/setup", cookies);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/2fa/confirm", cookies, new ConfirmTwoFactorSetupRequest("000000"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_AfterEnablingTwoFactor_RequiresACode()
    {
        var (email, password, secret, _) = await RegisterAndEnableTwoFactorAsync();
        var client = TestRequests.CreateClient(_fixture);

        var withoutCode = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.Unauthorized, withoutCode.StatusCode);

        var withValidCode = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, password, ComputeCode(secret)));
        Assert.Equal(HttpStatusCode.OK, withValidCode.StatusCode);
    }

    [Fact]
    public async Task Login_WithABackupCode_SucceedsAndTheCodeCanOnlyBeUsedOnce()
    {
        var (email, password, _, backupCodes) = await RegisterAndEnableTwoFactorAsync();
        var client = TestRequests.CreateClient(_fixture);
        var backupCode = backupCodes[0];

        var firstUse = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, password, backupCode));
        Assert.Equal(HttpStatusCode.OK, firstUse.StatusCode);

        var secondUse = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, password, backupCode));
        Assert.Equal(HttpStatusCode.Unauthorized, secondUse.StatusCode);
    }

    [Fact]
    public async Task Disable_WithTheCorrectPassword_LetsThePersonLogInWithoutACodeAgain()
    {
        var (email, password, secret, _) = await RegisterAndEnableTwoFactorAsync();
        var client = TestRequests.CreateClient(_fixture);
        var sessionCookies = new CookieJar();
        await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", sessionCookies, new LoginRequest(email, password, ComputeCode(secret)));

        var disableResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/2fa/disable", sessionCookies, new DisableTwoFactorRequest(password));
        Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

        var loginResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task RegenerateBackupCodes_InvalidatesThePreviousBatch()
    {
        var (email, password, secret, backupCodes) = await RegisterAndEnableTwoFactorAsync();
        var client = TestRequests.CreateClient(_fixture);
        var sessionCookies = new CookieJar();
        await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", sessionCookies, new LoginRequest(email, password, ComputeCode(secret)));

        var regenerateResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/2fa/backup-codes/regenerate", sessionCookies);
        Assert.Equal(HttpStatusCode.OK, regenerateResponse.StatusCode);
        var newCodes = await regenerateResponse.Content.ReadFromJsonAsync<List<string>>();
        Assert.DoesNotContain(backupCodes[0], newCodes!);

        var loginWithOldCode = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, password, backupCodes[0]));
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithOldCode.StatusCode);

        var loginWithNewCode = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/login", new CookieJar(), new LoginRequest(email, password, newCodes![0]));
        Assert.Equal(HttpStatusCode.OK, loginWithNewCode.StatusCode);
    }

    [Fact]
    public async Task UpdateMfaPolicy_ByAnAgent_ReturnsForbidden()
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

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Patch, $"/api/tenants/{tenantId}/settings/mfa-policy", agentCookies, new UpdateMfaPolicyRequest(true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMfaPolicy_ByTheOwner_Succeeds()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Patch, $"/api/tenants/{tenantId}/settings/mfa-policy", ownerCookies, new UpdateMfaPolicyRequest(true));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static string ComputeCode(string base32Secret) => new Totp(Base32Encoding.ToBytes(base32Secret)).ComputeTotp();

    private async Task<(string Email, string Password, string Secret, List<string> BackupCodes)> RegisterAndEnableTwoFactorAsync()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (email, password) = await TestRequests.RegisterAsync(client);
        var cookies = new CookieJar();
        await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/login", cookies, new LoginRequest(email, password));

        var setupResponse = await TestRequests.SendAsync(client, HttpMethod.Post, "/api/auth/2fa/setup", cookies);
        var setup = await setupResponse.Content.ReadFromJsonAsync<TwoFactorSetupResult>(TestRequests.JsonOptions);

        var confirmResponse = await TestRequests.SendAsync(
            client, HttpMethod.Post, "/api/auth/2fa/confirm", cookies, new ConfirmTwoFactorSetupRequest(ComputeCode(setup!.Secret)));
        var backupCodes = await confirmResponse.Content.ReadFromJsonAsync<List<string>>();

        return (email, password, setup.Secret, backupCodes!);
    }
}
