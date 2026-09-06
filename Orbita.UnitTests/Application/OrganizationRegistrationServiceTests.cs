using Moq;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class OrganizationRegistrationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly RegisterOrganizationRequest ValidRequest =
        new("Acme Corp", "Jane Doe", "jane@acme.com", "correct-horse-battery");

    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IMembershipRepository> _memberships = new();
    private readonly Mock<ITenantContextSetter> _tenantContextSetter = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly OrganizationRegistrationService _sut;

    public OrganizationRegistrationServiceTests()
    {
        _tenants.Setup(t => t.SlugExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed-password");

        _sut = new OrganizationRegistrationService(
            _tenants.Object,
            _users.Object,
            _memberships.Object,
            _tenantContextSetter.Object,
            _unitOfWork.Object,
            _passwordHasher.Object,
            new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task RegisterAsync_WithNewEmail_CreatesTenantOwnerUserAndMembershipInOneSave()
    {
        var result = await _sut.RegisterAsync(ValidRequest, CancellationToken.None);

        Assert.Equal("acme-corp", result.TenantSlug);
        Assert.Equal("jane@acme.com", result.Email);
        Assert.Equal("Jane Doe", result.FullName);

        _tenants.Verify(t => t.AddAsync(It.Is<Tenant>(x => x.Slug == "acme-corp"), It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(u => u.AddAsync(It.Is<User>(x => x.Email == "jane@acme.com"), It.IsAny<CancellationToken>()), Times.Once);
        _memberships.Verify(
            m => m.AddAsync(It.Is<Membership>(x => x.Role == MemberRole.Owner && x.UserId == result.UserId), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_SetsTenantContextToNewTenantBeforeSaving()
    {
        var result = await _sut.RegisterAsync(ValidRequest, CancellationToken.None);

        _tenantContextSetter.Verify(s => s.SetTenant(result.TenantId), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_HashesThePlainPasswordBeforePersisting()
    {
        await _sut.RegisterAsync(ValidRequest, CancellationToken.None);

        _passwordHasher.Verify(h => h.Hash("correct-horse-battery"), Times.Once);
        _users.Verify(u => u.AddAsync(It.Is<User>(x => x.PasswordHash == "hashed-password"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_NormalizesEmailToLowercaseBeforeCheckingAndPersisting()
    {
        var request = ValidRequest with { Email = "Jane@ACME.com" };

        await _sut.RegisterAsync(request, CancellationToken.None);

        _users.Verify(u => u.EmailExistsAsync("jane@acme.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ThrowsAndDoesNotPersistAnything()
    {
        _users.Setup(u => u.EmailExistsAsync("jane@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<EmailAlreadyRegisteredException>(
            () => _sut.RegisterAsync(ValidRequest, CancellationToken.None));

        _tenants.Verify(t => t.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()), Times.Never);
        _memberships.Verify(m => m.AddAsync(It.IsAny<Membership>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WithTakenSlug_AppendsNumericSuffix()
    {
        _tenants.Setup(t => t.SlugExistsAsync("acme-corp", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(t => t.SlugExistsAsync("acme-corp-2", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _sut.RegisterAsync(ValidRequest, CancellationToken.None);

        Assert.Equal("acme-corp-2", result.TenantSlug);
    }
}
