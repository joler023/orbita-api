using Moq;
using Orbita.Application.Identity;
using Orbita.Application.Inbox;
using Orbita.Application.Media;
using Orbita.Domain.Inbox;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class MediaServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<IMediaUrlSigner> _urlSigner = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly MediaService _sut;

    public MediaServiceTests()
    {
        _urlSigner.Setup(s => s.CreateToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>())).Returns("signed-token");
        _sut = new MediaService(_conversations.Object, _messages.Object, _urlSigner.Object, _authorization.Object, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task CreateUploadUrlAsync_ForAnAllowedType_ReturnsAKeyUnderTheConversation()
    {
        var conversation = Conversation.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now);
        _conversations.Setup(c => c.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);

        var result = await _sut.CreateUploadUrlAsync(TenantId, CallerId, conversation.Id, new CreateUploadUrlRequest("image/jpeg", "foto.jpg", 1024), CancellationToken.None);

        Assert.StartsWith($"tenants/{TenantId:D}/conversations/{conversation.Id:D}/", result.Key);
        Assert.EndsWith(".jpg", result.Key);
        Assert.Equal("/api/media/signed-token", result.UploadUrl);
    }

    [Fact]
    public async Task CreateUploadUrlAsync_ForAnUnsupportedType_Throws()
    {
        var conversation = Conversation.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now);
        _conversations.Setup(c => c.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);

        await Assert.ThrowsAsync<UnsupportedMediaTypeException>(() =>
            _sut.CreateUploadUrlAsync(TenantId, CallerId, conversation.Id, new CreateUploadUrlRequest("application/zip", "a.zip", 10), CancellationToken.None));
    }

    [Fact]
    public async Task CreateUploadUrlAsync_ExceedingTheSizeLimitForItsType_Throws()
    {
        var conversation = Conversation.Open(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now);
        _conversations.Setup(c => c.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);

        await Assert.ThrowsAsync<MediaTooLargeException>(() =>
            _sut.CreateUploadUrlAsync(TenantId, CallerId, conversation.Id, new CreateUploadUrlRequest("image/jpeg", "foto.jpg", 10 * 1024 * 1024), CancellationToken.None));
    }

    [Fact]
    public async Task CreateUploadUrlAsync_ForOtherTenantConversation_Throws()
    {
        _conversations.Setup(c => c.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Conversation?)null);

        await Assert.ThrowsAsync<ConversationNotFoundException>(() =>
            _sut.CreateUploadUrlAsync(TenantId, CallerId, Guid.NewGuid(), new CreateUploadUrlRequest("image/jpeg", "foto.jpg", 10), CancellationToken.None));
    }

    [Fact]
    public async Task GetMessageMediaUrlAsync_ForAMessageWithMedia_ReturnsASignedUrl()
    {
        var message = Message.Inbound(TenantId, Guid.NewGuid(), "wamid.1", null, "image/jpeg", null, Now);
        message.MarkMediaStored("tenants/t/conversations/c/m.jpg", "image/jpeg");
        _messages.Setup(m => m.GetByIdAsync(message.Id, It.IsAny<CancellationToken>())).ReturnsAsync(message);

        var result = await _sut.GetMessageMediaUrlAsync(TenantId, CallerId, message.Id, CancellationToken.None);

        Assert.Equal("/api/media/signed-token", result.Url);
    }

    [Fact]
    public async Task GetMessageMediaUrlAsync_ForAMessageWithoutMedia_Throws()
    {
        var message = Message.Inbound(TenantId, Guid.NewGuid(), "wamid.1", "hola", null, null, Now);
        _messages.Setup(m => m.GetByIdAsync(message.Id, It.IsAny<CancellationToken>())).ReturnsAsync(message);

        await Assert.ThrowsAsync<MessageNotFoundException>(() =>
            _sut.GetMessageMediaUrlAsync(TenantId, CallerId, message.Id, CancellationToken.None));
    }
}
