using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Ai;
using Orbita.Domain.Ai;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests.Ai;

/// <summary>
/// ORB-C13 end to end, against a real Postgres with Row Level Security applying — which is
/// what proves one tenant's model choice cannot be read or changed by another.
/// </summary>
public sealed class ModelPreferencesApiTests(TenantsApiFixture fixture) : IClassFixture<TenantsApiFixture>
{
    private const string Provider = "openai-compatible";

    [Fact]
    public async Task Every_task_reports_the_model_it_would_actually_use()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var preferences = await ListAsync(client, cookies, tenantId);

        // One row per task, so the screen never has to guess what is unset.
        Assert.Equal(Enum.GetValues<LlmTask>().Length, preferences.Count);
        Assert.All(preferences, preference => Assert.False(string.IsNullOrWhiteSpace(preference.Model)));
        // Nothing overridden yet: these are the deployment defaults.
        Assert.All(preferences, preference => Assert.False(preference.IsTenantOverride));
    }

    [Fact]
    public async Task A_tenant_can_choose_its_own_model_and_it_sticks()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await SetAsync(client, cookies, tenantId, LlmTask.Draft, "google/gemini-3.5-flash-lite");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var draft = (await ListAsync(client, cookies, tenantId)).Single(p => p.Task == LlmTask.Draft);
        Assert.Equal("google/gemini-3.5-flash-lite", draft.Model);
        Assert.True(draft.IsTenantOverride);
    }

    [Fact]
    public async Task Choosing_a_model_for_one_task_leaves_the_others_alone()
    {
        // ORB-C13's whole point: a cheap model classifies while a better one drafts.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        await SetAsync(client, cookies, tenantId, LlmTask.Classify, "deepseek/deepseek-v4-flash");

        var preferences = await ListAsync(client, cookies, tenantId);

        Assert.True(preferences.Single(p => p.Task == LlmTask.Classify).IsTenantOverride);
        Assert.False(preferences.Single(p => p.Task == LlmTask.Draft).IsTenantOverride);
    }

    [Fact]
    public async Task Setting_the_same_task_twice_replaces_rather_than_duplicates()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        await SetAsync(client, cookies, tenantId, LlmTask.Draft, "google/gemini-3.5-flash-lite");
        await SetAsync(client, cookies, tenantId, LlmTask.Draft, "deepseek/deepseek-v4-flash");

        // A unique index backs this: two answers to the same question, with no rule for
        // which wins, would be worse than none.
        var draft = Assert.Single(await ListAsync(client, cookies, tenantId), p => p.Task == LlmTask.Draft);
        Assert.Equal("deepseek/deepseek-v4-flash", draft.Model);
    }

    [Fact]
    public async Task Clearing_a_choice_falls_back_to_the_default()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        await SetAsync(client, cookies, tenantId, LlmTask.Draft, "google/gemini-3.5-flash-lite");

        var cleared = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/ai-models/{Provider}/Draft", cookies);

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var draft = (await ListAsync(client, cookies, tenantId)).Single(p => p.Task == LlmTask.Draft);
        Assert.False(draft.IsTenantOverride);
        Assert.NotEqual("google/gemini-3.5-flash-lite", draft.Model);
    }

    [Fact]
    public async Task Clearing_something_never_set_is_not_an_error()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(
            client, HttpMethod.Delete, $"/api/tenants/{tenantId}/ai-models/{Provider}/Embed", cookies);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_change_takes_effect_immediately_for_the_person_who_made_it()
    {
        // Selection is cached; without invalidation on write, "I changed it and nothing
        // happened" becomes a support question.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        // Warm the cache first, then change it.
        await ListAsync(client, cookies, tenantId);
        await SetAsync(client, cookies, tenantId, LlmTask.Draft, "google/gemini-3.5-flash-lite");

        var draft = (await ListAsync(client, cookies, tenantId)).Single(p => p.Task == LlmTask.Draft);
        Assert.Equal("google/gemini-3.5-flash-lite", draft.Model);
    }

    [Fact]
    public async Task The_choice_applies_only_to_the_provider_it_names()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        await SetAsync(client, cookies, tenantId, LlmTask.Draft, "google/gemini-3.5-flash-lite");

        // Asking Ollama for a gateway's model id would be a guaranteed 404 from Ollama —
        // which is exactly why the preference is keyed by provider.
        var onOllama = (await ListAsync(client, cookies, tenantId, "ollama")).Single(p => p.Task == LlmTask.Draft);
        Assert.NotEqual("google/gemini-3.5-flash-lite", onOllama.Model);
        Assert.False(onOllama.IsTenantOverride);
    }

    [Fact]
    public async Task One_tenants_choice_never_reaches_another()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, cookiesA) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        await SetAsync(client, cookiesA, tenantA, LlmTask.Draft, "google/gemini-3.5-flash-lite");

        var (_, _, tenantB, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");

        var draft = (await ListAsync(client, cookiesB, tenantB)).Single(p => p.Task == LlmTask.Draft);

        Assert.False(draft.IsTenantOverride);
        Assert.NotEqual("google/gemini-3.5-flash-lite", draft.Model);
    }

    [Fact]
    public async Task A_caller_outside_the_tenant_is_refused()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantA, _) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (_, _, _, cookiesB) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Otra Empresa");

        var read = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantA}/ai-models/{Provider}", cookiesB);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        var write = await TestRequests.SendAsync(
            client,
            HttpMethod.Put,
            $"/api/tenants/{tenantA}/ai-models/{Provider}/Draft",
            cookiesB,
            new { model = "modelo-ajeno" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_choose_models_because_it_changes_the_bill()
    {
        // Owner only (ManageAiModels), unlike the rest of the assistant's configuration.
        // Enforced here, over HTTP, because hiding the entry in the dashboard is not access
        // control — ORB-A08's own rule, and the frontend's reason for asking.
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, ownerCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var (_, adminCookies) = await TestRequests.InviteAndAcceptAsync(
            fixture, client, tenantId, ownerCookies, Orbita.Domain.Identity.MemberRole.Admin);

        // Positive control: the owner can, so the 403 below is about the role.
        Assert.Equal(HttpStatusCode.OK, (await SetAsync(client, ownerCookies, tenantId, LlmTask.Draft, "modelo-del-dueño")).StatusCode);

        var read = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-models/{Provider}", adminCookies);
        var write = await SetAsync(client, adminCookies, tenantId, LlmTask.Draft, "modelo-del-admin");
        var clear = await TestRequests.SendAsync(client, HttpMethod.Delete, $"/api/tenants/{tenantId}/ai-models/{Provider}/Draft", adminCookies);

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clear.StatusCode);

        // And an Admin can still configure the assistant itself — the line is money, not AI.
        var agents = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-agents", adminCookies);
        Assert.Equal(HttpStatusCode.OK, agents.StatusCode);
    }

    [Fact]
    public async Task The_provider_catalog_names_what_the_route_accepts()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, "/api/ai-providers", cookies);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var providers = (await response.Content.ReadFromJsonAsync<IReadOnlyList<LlmProviderDto>>(TestRequests.JsonOptions))!;

        // Every name it lists is one the preference route actually takes — that is the
        // whole contract, so it is checked against the route rather than against a list.
        Assert.NotEmpty(providers);
        foreach (var provider in providers)
        {
            Assert.False(string.IsNullOrWhiteSpace(provider.DisplayName));
            var listed = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-models/{provider.Name}", cookies);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        }

        // At most one primary, and only a configured one can be it.
        Assert.True(providers.Count(p => p.IsPrimary) <= 1);
        Assert.All(providers.Where(p => p.IsPrimary), p => Assert.True(p.IsConfigured));
    }

    [Fact]
    public async Task An_empty_model_is_rejected()
    {
        var client = TestRequests.CreateClient(fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await SetAsync(client, cookies, tenantId, LlmTask.Draft, "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SetAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        LlmTask task,
        string model)
        => TestRequests.SendAsync(
            client,
            HttpMethod.Put,
            $"/api/tenants/{tenantId}/ai-models/{Provider}/{task}",
            cookies,
            new { model });

    private static async Task<IReadOnlyList<ModelPreferenceDto>> ListAsync(
        HttpClient client,
        CookieJar cookies,
        Guid tenantId,
        string providerName = Provider)
    {
        var response = await TestRequests.SendAsync(
            client, HttpMethod.Get, $"/api/tenants/{tenantId}/ai-models/{providerName}", cookies);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<ModelPreferenceDto>>(TestRequests.JsonOptions))!;
    }
}
