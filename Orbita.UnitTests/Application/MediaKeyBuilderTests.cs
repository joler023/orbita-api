using Orbita.Application.Media;

namespace Orbita.UnitTests.Application;

public sealed class MediaKeyBuilderTests
{
    [Fact]
    public void ForMessage_BuildsATenantNamespacedKeyWithTheRightExtension()
    {
        var tenantId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();

        var key = MediaKeyBuilder.ForMessage(tenantId, conversationId, mediaId, "image/jpeg");

        Assert.Equal($"tenants/{tenantId:D}/conversations/{conversationId:D}/{mediaId:D}.jpg", key);
    }

    [Fact]
    public void BelongsTo_AcceptsAKeyUnderItsOwnTenantPrefix()
    {
        var tenantId = Guid.NewGuid();
        var key = MediaKeyBuilder.ForMessage(tenantId, Guid.NewGuid(), Guid.NewGuid(), "image/png");

        Assert.True(MediaKeyBuilder.BelongsTo(key, tenantId));
    }

    [Fact]
    public void BelongsTo_RejectsOtherTenantPrefixAndTraversal()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantKey = MediaKeyBuilder.ForMessage(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "image/png");
        var traversalKey = $"tenants/{tenantId:D}/../{tenantId:D}/secret.jpg";

        Assert.False(MediaKeyBuilder.BelongsTo(otherTenantKey, tenantId));
        Assert.False(MediaKeyBuilder.BelongsTo(traversalKey, tenantId));
    }
}
