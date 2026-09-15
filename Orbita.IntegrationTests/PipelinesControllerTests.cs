using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Orbita.Application.Crm;
using Orbita.Domain.Crm;
using Orbita.Infrastructure.Persistence;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

public sealed class PipelinesControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public PipelinesControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Register_SeedsADefaultSalesPipeline()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/pipelines", cookies);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pipelines = await response.Content.ReadFromJsonAsync<List<PipelineSummary>>(TestRequests.JsonOptions);
        var pipeline = Assert.Single(pipelines!);
        Assert.Equal(DefaultSalesPipeline.PipelineName, pipeline.Name);
        Assert.True(pipeline.IsDefault);
        Assert.Equal(DefaultSalesPipeline.Stages.Count, pipeline.Stages.Count);
        Assert.Contains(pipeline.Stages, s => s is { Name: "Ganada", IsWon: true });
        Assert.Contains(pipeline.Stages, s => s is { Name: "Perdida", IsLost: true });
    }

    [Fact]
    public async Task List_ByAnOutsider_ReturnsForbidden()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, _) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Acme Corp");
        var (_, _, _, outsiderCookies) = await TestRequests.RegisterAndLogInOwnerAsync(client, "Other Business");

        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/pipelines", outsiderCookies);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AddsASecondPipelineWithAnInitialStage()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/pipelines",
            cookies,
            new CreatePipelineRequest("Renovaciones"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PipelineSummary>(TestRequests.JsonOptions);
        Assert.Equal("Renovaciones", created!.Name);
        Assert.False(created.IsDefault);
        Assert.Equal("Nuevo", Assert.Single(created.Stages).Name);
    }

    [Fact]
    public async Task Delete_TheOnlyPipeline_ReturnsConflict()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var pipelines = await ListPipelinesAsync(client, tenantId, cookies);

        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/tenants/{tenantId}/pipelines/{pipelines[0].Id}",
            cookies);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateStage_AndReorder_PersistsTheNewOrder()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var pipeline = (await ListPipelinesAsync(client, tenantId, cookies))[0];

        var created = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/pipelines/{pipeline.Id}/stages",
            cookies,
            new CreateStageRequest("En espera"));
        var afterCreate = await created.Content.ReadFromJsonAsync<PipelineSummary>(TestRequests.JsonOptions);
        var reversed = afterCreate!.Stages.OrderByDescending(s => s.SortOrder).Select(s => s.Id).ToArray();

        var reorder = await TestRequests.SendAsync(
            client,
            HttpMethod.Put,
            $"/api/tenants/{tenantId}/pipelines/{pipeline.Id}/stages/order",
            cookies,
            new ReorderStagesRequest(reversed));

        Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);
        var ordered = await reorder.Content.ReadFromJsonAsync<PipelineSummary>(TestRequests.JsonOptions);
        Assert.Equal(reversed, ordered!.Stages.Select(s => s.Id).ToArray());
    }

    [Fact]
    public async Task DeleteStage_WithOpportunities_RequiresRelocateThenMovesThem()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var pipeline = (await ListPipelinesAsync(client, tenantId, cookies))[0];
        var from = pipeline.Stages[0];
        var to = pipeline.Stages[1];
        await SeedOpportunityAsync(tenantId, pipeline.Id, from.Id);

        var blocked = await TestRequests.SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/tenants/{tenantId}/pipelines/{pipeline.Id}/stages/{from.Id}",
            cookies);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var moved = await TestRequests.SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/tenants/{tenantId}/pipelines/{pipeline.Id}/stages/{from.Id}?relocateToStageId={to.Id}",
            cookies);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var remaining = await moved.Content.ReadFromJsonAsync<PipelineSummary>(TestRequests.JsonOptions);
        Assert.DoesNotContain(remaining!.Stages, s => s.Id == from.Id);
    }

    private static async Task<List<PipelineSummary>> ListPipelinesAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/pipelines", cookies);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<PipelineSummary>>(TestRequests.JsonOptions))!;
    }

    private async Task SeedOpportunityAsync(Guid tenantId, Guid pipelineId, Guid stageId)
    {
        var tenantContext = new AmbientTenantContext();
        tenantContext.SetTenant(tenantId);
        var options = new DbContextOptionsBuilder<OrbitaDbContext>()
            .UseNpgsql(_fixture.GetAdminConnectionString())
            .Options;
        await using var dbContext = new OrbitaDbContext(options, tenantContext);
        dbContext.Opportunities.Add(Opportunity.Create(tenantId, pipelineId, stageId, "Sitio web", 500m, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
    }
}
