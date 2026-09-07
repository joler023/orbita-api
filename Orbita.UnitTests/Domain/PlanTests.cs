using Orbita.Domain.Billing;

namespace Orbita.UnitTests.Domain;

public sealed class PlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_AppliesDefaults()
    {
        var plan = Plan.Create("starter", "Starter", 500, 1000, 29m, "usd", Now);

        Assert.Equal("starter", plan.Code);
        Assert.Equal("Starter", plan.Name);
        Assert.Equal(500, plan.IncludedConversations);
        Assert.Equal(1000, plan.IncludedAiCredits);
        Assert.Equal(29m, plan.PriceAmount);
        Assert.Equal("USD", plan.PriceCurrency);
        Assert.Null(plan.StripePriceId);
        Assert.True(plan.IsActive);
    }

    [Fact]
    public void SetStripePriceId_StoresTheId()
    {
        var plan = Plan.Create("starter", "Starter", 500, 1000, 29m, "usd", Now);

        plan.SetStripePriceId("price_123");

        Assert.Equal("price_123", plan.StripePriceId);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var plan = Plan.Create("starter", "Starter", 500, 1000, 29m, "usd", Now);

        plan.Deactivate();

        Assert.False(plan.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("us")]
    [InlineData("usdd")]
    public void Create_WithInvalidCurrencyLength_Throws(string currency)
    {
        Assert.Throws<ArgumentException>(() => Plan.Create("starter", "Starter", 500, 1000, 29m, currency, Now));
    }
}
