using Moq;
using Orbita.Application.Audit;
using Orbita.Application.Channels;
using Orbita.Application.Identity;
using Orbita.Domain.Channels;
using Orbita.Domain.Common;
using Orbita.Domain.Inbox;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class MessageTemplateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    private readonly Mock<IMessageTemplateRepository> _templates = new();
    private readonly Mock<IChannelAccountRepository> _channelAccounts = new();
    private readonly Mock<IChannelCredentialStore> _credentialStore = new();
    private readonly Mock<IWhatsAppCloudApiClient> _whatsAppClient = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly MessageTemplateService _sut;
    private readonly ChannelAccount _account;

    public MessageTemplateServiceTests()
    {
        _account = ChannelAccount.ConnectWhatsApp(TenantId, "phone-1", "waba-1", "Acme", null, "local://a", null, Now);
        _channelAccounts.Setup(a => a.GetByIdAsync(_account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_account);
        _credentialStore.Setup(c => c.GetAsync(_account.CredentialsRef, It.IsAny<CancellationToken>())).ReturnsAsync("token");

        _sut = new MessageTemplateService(
            _templates.Object, _channelAccounts.Object, _credentialStore.Object, _whatsAppClient.Object,
            _authorization.Object, _auditLogger.Object, _unitOfWork.Object, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task CreateAsync_RegistersANewTemplate()
    {
        _templates.Setup(t => t.FindByNameAsync(_account.Id, "greeting", "es", It.IsAny<CancellationToken>())).ReturnsAsync((MessageTemplate?)null);

        var result = await _sut.CreateAsync(TenantId, CallerId, new CreateTemplateRequest(_account.Id, "greeting", MessageCategory.Utility, "es", "Hola"), CancellationToken.None);

        Assert.Equal("greeting", result.MetaTemplateName);
        Assert.Equal(TemplateStatus.Draft, result.Status);
        _templates.Verify(t => t.AddAsync(It.IsAny<MessageTemplate>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenAlreadyRegistered_Throws()
    {
        var existing = MessageTemplate.Create(TenantId, _account.Id, "greeting", MessageCategory.Utility, "es", "Hola", Now);
        _templates.Setup(t => t.FindByNameAsync(_account.Id, "greeting", "es", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        await Assert.ThrowsAsync<TemplateAlreadyExistsException>(() =>
            _sut.CreateAsync(TenantId, CallerId, new CreateTemplateRequest(_account.Id, "greeting", MessageCategory.Utility, "es", "Hola"), CancellationToken.None));
    }

    [Fact]
    public async Task SyncFromMetaAsync_AppliesStatusesAndCreatesMissingTemplates()
    {
        _whatsAppClient
            .Setup(c => c.ListTemplatesAsync("token", "waba-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WhatsAppTemplateInfo>
            {
                new("greeting", "es", "APPROVED", null),
                new("promo", "es", "REJECTED", "policy violation"),
            });
        _templates.Setup(t => t.FindByNameAsync(_account.Id, It.IsAny<string>(), "es", It.IsAny<CancellationToken>())).ReturnsAsync((MessageTemplate?)null);

        var count = await _sut.SyncFromMetaAsync(TenantId, CallerId, _account.Id, CancellationToken.None);

        Assert.Equal(2, count);
        _templates.Verify(t => t.AddAsync(It.IsAny<MessageTemplate>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncFromMetaAsync_ForAnExistingTemplate_UpdatesStatusInPlace()
    {
        var existing = MessageTemplate.Create(TenantId, _account.Id, "greeting", MessageCategory.Utility, "es", "Hola", Now);
        _whatsAppClient
            .Setup(c => c.ListTemplatesAsync("token", "waba-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WhatsAppTemplateInfo> { new("greeting", "es", "APPROVED", null) });
        _templates.Setup(t => t.FindByNameAsync(_account.Id, "greeting", "es", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        await _sut.SyncFromMetaAsync(TenantId, CallerId, _account.Id, CancellationToken.None);

        Assert.True(existing.IsApproved);
        _templates.Verify(t => t.AddAsync(It.IsAny<MessageTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
