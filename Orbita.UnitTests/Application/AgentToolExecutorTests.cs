using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orbita.Application.Ai;
using Orbita.Application.Crm;
using Orbita.Domain.Ai;

namespace Orbita.UnitTests.Application;

/// <summary>
/// ORB-C05. The executor is the single place a model's arguments become a real change
/// in the CRM, so these tests are about what it refuses as much as what it does.
/// </summary>
public sealed class AgentToolExecutorTests
{
    private readonly Mock<IOpportunityService> _opportunities = new();
    private readonly AgentToolExecutor _sut;

    private readonly AgentToolContext _context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    public AgentToolExecutorTests()
        => _sut = new AgentToolExecutor(_opportunities.Object, NullLogger<AgentToolExecutor>.Instance);

    private static OpportunitySummary Summary(string title)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), title, null, null, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Creating_an_opportunity_always_uses_the_conversations_tenant_and_contact()
    {
        _opportunities
            .Setup(o => o.CreateForAgentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Summary("Torta"));

        // The model tries to point at another tenant and another contact. Nothing reads them.
        var call = new LlmToolCall(
            "c1",
            AiToolCatalog.CrearOportunidad,
            $$"""{"titulo":"Torta","monto":120000,"tenantId":"{{Guid.NewGuid()}}","contactId":"{{Guid.NewGuid()}}"}""");

        var result = await _sut.ExecuteAsync(_context, call, CancellationToken.None);

        Assert.True(result.Succeeded);
        _opportunities.Verify(o => o.CreateForAgentAsync(
            _context.TenantId, _context.AgentId, _context.ContactId, "Torta", 120000m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"titulo":"   "}""")]
    [InlineData("""{"titulo":"Torta","monto":-5}""")]
    [InlineData("""{"titulo":"Torta","monto":"mucho"}""")]
    [InlineData("""no es json""")]
    [InlineData("""["titulo"]""")]
    public async Task Invalid_arguments_fail_the_tool_without_touching_the_crm(string arguments)
    {
        var result = await _sut.ExecuteAsync(_context, new LlmToolCall("c1", AiToolCatalog.CrearOportunidad, arguments), CancellationToken.None);

        Assert.False(result.Succeeded);
        _opportunities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_title_past_the_limit_is_refused()
    {
        var longTitle = new string('a', AgentToolExecutor.TitleMaxLength + 1);

        var result = await _sut.ExecuteAsync(
            _context, new LlmToolCall("c1", AiToolCatalog.CrearOportunidad, $$"""{"titulo":"{{longTitle}}"}"""), CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_failing_service_becomes_a_failed_result_not_an_exception()
    {
        // "Una herramienta que falla no rompe la conversación."
        _opportunities
            .Setup(o => o.CreateForAgentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is down"));

        var result = await _sut.ExecuteAsync(
            _context, new LlmToolCall("c1", AiToolCatalog.CrearOportunidad, """{"titulo":"Torta"}"""), CancellationToken.None);

        Assert.False(result.Succeeded);

        // The internal error never reaches the model, and so never reaches the customer.
        Assert.DoesNotContain("database", result.ResultJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Moving_to_a_stage_that_does_not_exist_says_so_in_words()
    {
        _opportunities
            .Setup(o => o.MoveContactOpportunityForAgentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StageNotFoundException());

        var result = await _sut.ExecuteAsync(
            _context, new LlmToolCall("c1", AiToolCatalog.MoverEtapa, """{"etapa":"Volando"}"""), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Volando", result.ResultJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tool_the_executor_does_not_know_is_refused()
    {
        var result = await _sut.ExecuteAsync(
            _context, new LlmToolCall("c1", AiToolCatalog.AgendarCita, """{}"""), CancellationToken.None);

        Assert.False(result.Succeeded);
        _opportunities.VerifyNoOtherCalls();
    }

    [Fact]
    public void Only_enabled_executable_tools_are_offered_to_the_model()
    {
        var agent = AiAgent.Create(Guid.NewGuid(), "A", "B", "C", AgentStyle.Default, DateTimeOffset.UtcNow);
        agent.EnableTools([AiToolCatalog.ConsultarConocimiento, AiToolCatalog.CrearOportunidad]);

        var definitions = _sut.DefinitionsFor(agent);

        // consultar_conocimiento runs as retrieval before the call, not as a callable tool.
        Assert.Equal([AiToolCatalog.CrearOportunidad], definitions.Select(d => d.Name));
    }
}
