using Orbita.Application.Channels;

namespace Orbita.UnitTests.Application;

public sealed class MetaErrorCatalogTests
{
    [Theory]
    [InlineData("131047", false)]
    [InlineData("131026", false)]
    [InlineData("130429", true)]
    [InlineData("131056", true)]
    [InlineData("100", false)]
    [InlineData("190", false)]
    [InlineData("131001", false)]
    [InlineData("133010", false)]
    [InlineData("470", false)]
    [InlineData("131053", false)]
    [InlineData("unknown-code", false)]
    public void Describe_ClassifiesTransience(string code, bool expectedTransient)
    {
        var (isTransient, message) = MetaErrorCatalog.Describe(code);

        Assert.Equal(expectedTransient, isTransient);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void Describe_TokenExpiredCodes_MentionsReconnecting()
    {
        var (_, message190) = MetaErrorCatalog.Describe("190");
        var (_, message131001) = MetaErrorCatalog.Describe("131001");

        Assert.Contains("token", message190, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(message190, message131001);
    }
}
