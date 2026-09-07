using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Billing;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

public sealed class PlansControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public PlansControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task List_ReturnsTheSeededCatalogWithoutRequiringAuthentication()
    {
        var client = TestRequests.CreateClient(_fixture);

        var response = await client.GetAsync("/api/plans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plans = await response.Content.ReadFromJsonAsync<List<PlanDto>>();
        Assert.Contains(plans!, p => p.Code == "starter");
        Assert.Contains(plans!, p => p.Code == "growth");
        Assert.Contains(plans!, p => p.Code == "scale");
    }
}
