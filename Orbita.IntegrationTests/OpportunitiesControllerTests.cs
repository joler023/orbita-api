using System.Net;
using System.Net.Http.Json;
using Orbita.Application.Crm;
using Orbita.IntegrationTests.TestSupport;

namespace Orbita.IntegrationTests;

public sealed class OpportunitiesControllerTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public OpportunitiesControllerTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_ThenBoard_ShowsTheCardAndStageTotal()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var pipeline = await GetDefaultPipelineAsync(client, tenantId, cookies);
        var stageId = pipeline.Stages[0].Id;

        var created = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/pipelines/{pipeline.Id}/opportunities",
            cookies,
            new CreateOpportunityRequest("Sitio web", 1_500m, stageId, null));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var boardResponse = await TestRequests.SendAsync(
            client,
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/pipelines/{pipeline.Id}/board",
            cookies);
        var board = await boardResponse.Content.ReadFromJsonAsync<PipelineBoard>(TestRequests.JsonOptions);

        Assert.Equal(1_500m, board!.Stages[0].AmountSum);
        Assert.Equal("Sitio web", Assert.Single(board.Stages[0].Opportunities).Title);
    }

    [Fact]
    public async Task Move_TwiceWithTheSameEventId_StaysIdempotent()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var pipeline = await GetDefaultPipelineAsync(client, tenantId, cookies);
        var create = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/pipelines/{pipeline.Id}/opportunities",
            cookies,
            new CreateOpportunityRequest("Sitio web", 800m, pipeline.Stages[0].Id, null));
        var card = await create.Content.ReadFromJsonAsync<OpportunitySummary>(TestRequests.JsonOptions);
        var eventId = Guid.NewGuid();
        var destination = pipeline.Stages[1].Id;

        var first = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/opportunities/{card!.Id}/move",
            cookies,
            new MoveOpportunityRequest(destination, eventId));
        var second = await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/opportunities/{card.Id}/move",
            cookies,
            new MoveOpportunityRequest(destination, eventId));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var moved = await second.Content.ReadFromJsonAsync<OpportunitySummary>(TestRequests.JsonOptions);
        Assert.Equal(destination, moved!.StageId);
        Assert.Equal(eventId, moved.LastMoveEventId);
    }

    [Fact]
    public async Task Board_FilterByDate_HidesOlderCards()
    {
        var client = TestRequests.CreateClient(_fixture);
        var (_, _, tenantId, cookies) = await TestRequests.RegisterAndLogInOwnerAsync(client);
        var pipeline = await GetDefaultPipelineAsync(client, tenantId, cookies);
        await TestRequests.SendAsync(
            client,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/pipelines/{pipeline.Id}/opportunities",
            cookies,
            new CreateOpportunityRequest("Sitio web", 100m, null, null));

        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var response = await TestRequests.SendAsync(
            client,
            HttpMethod.Get,
            $"/api/tenants/{tenantId}/pipelines/{pipeline.Id}/board?createdFrom={future}",
            cookies);
        var board = await response.Content.ReadFromJsonAsync<PipelineBoard>(TestRequests.JsonOptions);

        Assert.All(board!.Stages, stage => Assert.Empty(stage.Opportunities));
    }

    private static async Task<PipelineSummary> GetDefaultPipelineAsync(HttpClient client, Guid tenantId, CookieJar cookies)
    {
        var response = await TestRequests.SendAsync(client, HttpMethod.Get, $"/api/tenants/{tenantId}/pipelines", cookies);
        var pipelines = await response.Content.ReadFromJsonAsync<List<PipelineSummary>>(TestRequests.JsonOptions);
        return pipelines![0];
    }
}
