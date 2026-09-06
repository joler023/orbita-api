using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Identity;

namespace Orbita.IntegrationTests;

public sealed class OrganizationsControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly HttpClient _client;

    public OrganizationsControllerTests(TenantsApiFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task Register_WithNewBusiness_CreatesOrganizationAndOwner()
    {
        var request = new RegisterOrganizationRequest(
            "Panadería El Sol",
            "María Pérez",
            $"maria-{Guid.NewGuid():N}@elsol.com",
            "correct-horse-battery");

        var response = await _client.PostAsJsonAsync("/api/organizations", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RegisterOrganizationResult>();
        Assert.NotNull(result);
        Assert.Equal("panaderia-el-sol", result!.TenantSlug);
        Assert.Equal(request.Email.ToLowerInvariant(), result.Email);
        Assert.Equal(request.FullName, result.FullName);

        var getTenant = await _client.GetAsync($"/api/tenants/{result.TenantId}");
        Assert.Equal(HttpStatusCode.OK, getTenant.StatusCode);
    }

    [Fact]
    public async Task Register_TwiceWithSameBusinessName_GetsDistinctSlugs()
    {
        var businessName = $"Duplicada {Guid.NewGuid():N}";

        var first = await RegisterAsync(businessName);
        var second = await RegisterAsync(businessName);

        Assert.NotEqual(first.TenantSlug, second.TenantSlug);
    }

    [Fact]
    public async Task Register_WithEmailAlreadyUsed_ReturnsConflict()
    {
        var email = $"repeat-{Guid.NewGuid():N}@acme.com";
        await _client.PostAsJsonAsync(
            "/api/organizations",
            new RegisterOrganizationRequest("Acme Corp", "Jane Doe", email, "correct-horse-battery"));

        var response = await _client.PostAsJsonAsync(
            "/api/organizations",
            new RegisterOrganizationRequest("Another Corp", "John Doe", email, "another-password"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<RegisterOrganizationResult> RegisterAsync(string businessName)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/organizations",
            new RegisterOrganizationRequest(businessName, "Jane Doe", $"{Guid.NewGuid():N}@acme.com", "correct-horse-battery"));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterOrganizationResult>())!;
    }
}
