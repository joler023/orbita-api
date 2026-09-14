using Moq;
using Orbita.Application.Crm;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class ExportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IOpportunityRepository> _opportunities = new();
    private readonly Mock<IPipelineRepository> _pipelines = new();
    private readonly Mock<IPipelineStageRepository> _stages = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly ExportService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    public ExportServiceTests()
    {
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Contact>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<Contact>>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Opportunity>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<Opportunity>>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Pipeline>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<Pipeline>>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<PipelineStage>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<PipelineStage>>> query, CancellationToken ct) => query(ct));

        _sut = new ExportService(
            _contacts.Object,
            _opportunities.Object,
            _pipelines.Object,
            _stages.Object,
            _authorization.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task ExportContactsAsync_RequiresViewContactsAndReturnsCsv()
    {
        var contact = Contact.Create(_tenantId, "Ana Pérez", Now, "+573001112233", null, "ana@example.com", "whatsapp");
        _contacts
            .Setup(r => r.ListForExportAsync(_tenantId, 10_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync([contact]);

        var file = await _sut.ExportContactsAsync(_tenantId, _callerId, CancellationToken.None);

        _authorization.Verify(
            a => a.EnsurePermissionAsync(_tenantId, _callerId, Permission.ViewContacts, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.StartsWith("orbita-contacts-", file.FileName);
        var text = System.Text.Encoding.UTF8.GetString(file.Content);
        Assert.Contains("Ana Pérez", text, StringComparison.Ordinal);
        Assert.Contains("+573001112233", text, StringComparison.Ordinal);
    }
}
