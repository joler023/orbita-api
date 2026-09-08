using Moq;
using Orbita.Application.Audit;
using Orbita.Application.Crm;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Crm;
using Orbita.Domain.Identity;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class ContactServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 15, 0, 0, TimeSpan.Zero);

    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IContactFieldDefinitionRepository> _fields = new();
    private readonly Mock<IOpportunityRepository> _opportunities = new();
    private readonly Mock<IPipelineRepository> _pipelines = new();
    private readonly Mock<IPipelineStageRepository> _stages = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITenantAuthorizationService> _authorization = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly ContactService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    public ContactServiceTests()
    {
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<Contact?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Contact?>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Contact>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<Contact>>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Opportunity>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<Opportunity>>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<PipelineStage?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<PipelineStage?>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<Pipeline?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Pipeline?>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<ContactFieldDefinition?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<ContactFieldDefinition?>> query, CancellationToken ct) => query(ct));
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<ContactFieldDefinition>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<ContactFieldDefinition>>> query, CancellationToken ct) => query(ct));

        _sut = new ContactService(
            _contacts.Object,
            _fields.Object,
            _opportunities.Object,
            _pipelines.Object,
            _stages.Object,
            _users.Object,
            _authorization.Object,
            _audit.Object,
            _unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task CreateAsync_PersistsWhenPhoneIsNew()
    {
        _contacts
            .Setup(r => r.FindDuplicateAsync(_tenantId, "573001112233", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Contact?)null);

        var created = await _sut.CreateAsync(
            _tenantId,
            _callerId,
            new CreateContactRequest("Ana Pérez", "+57 300 111 2233", null, null, "whatsapp", null),
            CancellationToken.None);

        Assert.Equal("Ana Pérez", created.DisplayName);
        Assert.Equal("573001112233", created.Phone);
        _contacts.Verify(r => r.AddAsync(It.IsAny<Contact>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicatePhone_Throws()
    {
        var existing = Contact.Create(_tenantId, "Otra", Now, phone: "+573001112233");
        _contacts
            .Setup(r => r.FindDuplicateAsync(_tenantId, "573001112233", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await Assert.ThrowsAsync<ContactAlreadyExistsException>(() =>
            _sut.CreateAsync(
                _tenantId,
                _callerId,
                new CreateContactRequest("Ana", "+57 300 111 2233", null, null, null, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_FillsStageFromLatestDeal()
    {
        var contact = Contact.Create(_tenantId, "Ana", Now, phone: "+57300");
        var pipeline = Pipeline.Create(_tenantId, "Ventas", true, Now);
        var stage = PipelineStage.Create(_tenantId, pipeline.Id, "Propuesta", 2, false, false, Now);
        var deal = Opportunity.Create(_tenantId, pipeline.Id, stage.Id, "Sitio", 1_500m, Now, contactId: contact.Id);
        deal.LinkContact(contact.Id, Now);

        _contacts
            .Setup(r => r.SearchAsync(_tenantId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Contact>)[contact]);
        _opportunities
            .Setup(r => r.GetByContactIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Opportunity>)[deal]);
        _stages.Setup(r => r.GetByIdAsync(stage.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stage);

        var rows = await _sut.SearchAsync(_tenantId, _callerId, null, null, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("Propuesta", row.StageName);
        Assert.Equal(1_500m, row.Amount);
    }

    [Fact]
    public async Task CreateFieldAsync_DuplicateKey_Throws()
    {
        var existing = ContactFieldDefinition.Create(_tenantId, "ciudad", "Ciudad", ContactFieldType.Text, Now);
        _fields
            .Setup(r => r.GetByKeyAsync(_tenantId, "ciudad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await Assert.ThrowsAsync<ContactFieldAlreadyExistsException>(() =>
            _sut.CreateFieldAsync(
                _tenantId,
                _callerId,
                new CreateContactFieldRequest("ciudad", "Ciudad", "Text"),
                CancellationToken.None));
    }
}
