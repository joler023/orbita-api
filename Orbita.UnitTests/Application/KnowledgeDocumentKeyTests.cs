using Orbita.Application.Ai;

namespace Orbita.UnitTests.Application;

/// <summary>
/// The tenant prefix is what makes <see cref="KnowledgeDocumentKey.BelongsTo"/> a real
/// isolation check rather than decoration, so it is pinned here — same reasoning as
/// ORB-B06's MediaKeyBuilder, whose shape this follows.
/// </summary>
public sealed class KnowledgeDocumentKeyTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void A_key_is_namespaced_under_its_tenant()
    {
        var key = KnowledgeDocumentKey.For(TenantId, Guid.NewGuid(), ".pdf");

        Assert.StartsWith($"tenants/{TenantId:D}/knowledge/", key, StringComparison.Ordinal);
        Assert.EndsWith(".pdf", key, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_documents_never_share_a_key()
    {
        var first = KnowledgeDocumentKey.For(TenantId, Guid.NewGuid(), ".pdf");
        var second = KnowledgeDocumentKey.For(TenantId, Guid.NewGuid(), ".pdf");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData(".PDF")]
    [InlineData(" .pdf ")]
    public void The_extension_is_normalized_however_it_arrives(string extension)
        => Assert.EndsWith(".pdf", KnowledgeDocumentKey.For(TenantId, Guid.NewGuid(), extension), StringComparison.Ordinal);

    [Fact]
    public void A_document_with_no_extension_still_gets_a_key()
        => Assert.DoesNotContain('.', KnowledgeDocumentKey.For(TenantId, Guid.NewGuid(), "   ").Split('/')[^1]);

    [Fact]
    public void A_key_belongs_only_to_the_tenant_it_names()
    {
        var key = KnowledgeDocumentKey.For(TenantId, Guid.NewGuid(), ".pdf");

        Assert.True(KnowledgeDocumentKey.BelongsTo(key, TenantId));
        Assert.False(KnowledgeDocumentKey.BelongsTo(key, Guid.NewGuid()));
    }

    [Fact]
    public void A_key_that_tries_to_climb_out_of_its_prefix_is_rejected()
    {
        // Belt and braces: the storage implementation also refuses to resolve outside its
        // root, but a key is data that arrived through an API and is never trusted twice.
        var traversal = $"tenants/{TenantId:D}/../{Guid.NewGuid():D}/secreto.pdf";

        Assert.False(KnowledgeDocumentKey.BelongsTo(traversal, TenantId));
    }
}
