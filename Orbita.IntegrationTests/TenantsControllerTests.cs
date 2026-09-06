using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Tenants;

namespace Orbita.IntegrationTests;

public sealed class TenantsControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly HttpClient _client;

    public TenantsControllerTests(TenantsApiFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task CreateThenGet_RoundTripsTenant()
    {
        var request = new CreateTenantRequest($"acme-{Guid.NewGuid():N}", "Acme Corp");

        var createResponse = await _client.PostAsJsonAsync("/api/tenants", request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(created);
        Assert.Equal(request.Slug, created!.Slug);

        var getResponse = await _client.GetAsync(createResponse.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<TenantDto>();
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task Create_WithDuplicateSlug_ReturnsConflict()
    {
        var slug = $"acme-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/tenants", new CreateTenantRequest(slug, "Acme Corp"));

        var response = await _client.PostAsJsonAsync("/api/tenants", new CreateTenantRequest(slug, "Another"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetById_WhenMissing_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/tenants/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
