using Moq;
using Orbita.Application.Tenants;
using Orbita.Domain.Tenants;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class TenantServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITenantRepository> _repository = new();
    private readonly TenantService _sut;

    public TenantServiceTests()
    {
        _sut = new TenantService(_repository.Object, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task CreateAsync_WithNewSlug_PersistsAndReturnsDto()
    {
        _repository.Setup(r => r.SlugExistsAsync("acme", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _sut.CreateAsync(new CreateTenantRequest("acme", "Acme"), CancellationToken.None);

        Assert.Equal("acme", result.Slug);
        Assert.Equal(Now, result.CreatedAt);
        _repository.Verify(
            r => r.AddAsync(It.Is<Tenant>(t => t.Slug == "acme"), It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithExistingSlug_ThrowsAndDoesNotPersist()
    {
        _repository.Setup(r => r.SlugExistsAsync("acme", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<TenantSlugAlreadyExistsException>(
            () => _sut.CreateAsync(new CreateTenantRequest("acme", "Acme"), CancellationToken.None));

        _repository.Verify(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_WhenMissing_ReturnsNull()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await _sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_WhenFound_ReturnsMatchingDto()
    {
        var tenant = Tenant.Create("acme", "Acme", now: Now);
        _repository.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var result = await _sut.GetByIdAsync(tenant.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(tenant.Id, result!.Id);
        Assert.Equal(tenant.Slug, result.Slug);
    }
}
