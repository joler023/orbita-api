using Orbita.Application.Ai;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

/// <summary>
/// ORB-C13's provider catalog. The contract a screen relies on is the order (it is the
/// order calls are tried in) and which one is actually answering today.
/// </summary>
public sealed class LlmProviderCatalogTests
{
    [Fact]
    public void Providers_are_listed_in_failover_order_with_the_first_configured_one_as_primary()
    {
        var catalog = new LlmProviderCatalog(
        [
            new StubLlmProvider("openai-compatible") { IsConfigured = false },
            new StubLlmProvider("ollama") { IsConfigured = true },
        ]);

        var providers = catalog.List();

        Assert.Equal(["openai-compatible", "ollama"], providers.Select(p => p.Name));

        // The first one is skipped at runtime when unconfigured, so it is not the one
        // answering — marking it primary would tell the owner the wrong thing.
        Assert.False(providers[0].IsPrimary);
        Assert.True(providers[1].IsPrimary);
    }

    [Fact]
    public void Nothing_is_primary_when_nothing_is_configured()
    {
        var catalog = new LlmProviderCatalog([new StubLlmProvider("openai-compatible") { IsConfigured = false }]);

        Assert.DoesNotContain(catalog.List(), p => p.IsPrimary);
    }

    [Fact]
    public void An_unknown_provider_is_shown_by_its_own_name_rather_than_hidden()
    {
        var catalog = new LlmProviderCatalog([new StubLlmProvider("groq")]);

        Assert.Equal("groq", Assert.Single(catalog.List()).DisplayName);
    }
}
