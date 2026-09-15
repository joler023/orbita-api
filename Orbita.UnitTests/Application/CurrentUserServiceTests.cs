using Moq;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;

namespace Orbita.UnitTests.Application;

/// <summary>
/// ORB-A16. The shape the application shell binds to right after signing in.
/// </summary>
public sealed class CurrentUserServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IMembershipRepository> _memberships = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly User _user;
    private readonly CurrentUserService _sut;

    /// <summary>The id the user-scoped transaction was opened with, which is what RLS will trust.</summary>
    private Guid _scopedTo;

    public CurrentUserServiceTests()
    {
        _user = User.Create("demo@orbita.local", "hash", "Manuel Rodríguez", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);

        _unitOfWork
            .Setup(u => u.QueryInUserScopeAsync(
                It.IsAny<Guid>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Membership>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Guid userId, Func<CancellationToken, Task<IReadOnlyList<Membership>>> query, CancellationToken ct) =>
            {
                _scopedTo = userId;
                return query(ct);
            });

        _sut = new CurrentUserService(_users.Object, _memberships.Object, _tenants.Object, _unitOfWork.Object);
    }

    private Tenant NewTenant(string slug, string name)
        => Tenant.Create(slug, name, "CO", "America/Bogota", "es-CO", _now);

    private Membership Accepted(Tenant tenant, MemberRole role = MemberRole.Owner)
    {
        var membership = role == MemberRole.Owner
            ? Membership.CreateOwner(tenant.Id, _user.Id, _now)
            : Membership.Invite(tenant.Id, _user.Id, role, Guid.NewGuid(), _now);

        membership.Accept(_now);

        return membership;
    }

    private void Given(IReadOnlyList<Membership> memberships, params Tenant[] tenants)
    {
        _memberships
            .Setup(r => r.ListAcceptedByUserAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);
        _tenants
            .Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenants);
    }

    private Task<CurrentUser> GetAsync() => _sut.GetAsync(_user.Id, CancellationToken.None);

    [Fact]
    public async Task Who_is_signed_in_comes_back_with_where_they_can_act()
    {
        var tenant = NewTenant("panaderia-aurora", "Panadería Aurora");
        Given([Accepted(tenant)], tenant);

        var result = await GetAsync();

        Assert.Equal(_user.Id, result.UserId);
        Assert.Equal("demo@orbita.local", result.Email);
        Assert.Equal("Manuel Rodríguez", result.FullName);

        var membership = Assert.Single(result.Memberships);
        Assert.Equal(tenant.Id, membership.TenantId);
        Assert.Equal("panaderia-aurora", membership.Slug);
        Assert.Equal("Panadería Aurora", membership.Name);
        Assert.Equal(MemberRole.Owner, membership.Role);
    }

    [Fact]
    public async Task The_cross_tenant_read_is_scoped_to_the_signed_in_person()
    {
        // The id handed to the database is what the policy trusts. If it ever came from
        // anywhere but the session, this endpoint would read anyone's memberships.
        var tenant = NewTenant("panaderia-aurora", "Panadería Aurora");
        Given([Accepted(tenant)], tenant);

        await GetAsync();

        Assert.Equal(_user.Id, _scopedTo);
    }

    [Fact]
    public async Task Belonging_to_nothing_is_an_answer_not_an_error()
    {
        // Invited and then removed, or signed up without creating one. The client offers
        // to create an organization; it does not show a broken screen.
        Given([]);

        var result = await GetAsync();

        Assert.Empty(result.Memberships);
        Assert.Equal("demo@orbita.local", result.Email);
    }

    [Fact]
    public async Task Nothing_is_looked_up_when_there_is_nothing_to_look_up()
    {
        Given([]);

        await GetAsync();

        _tenants.Verify(
            r => r.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Several_organizations_come_back_in_a_stable_order()
    {
        // The client renders a picker from this; a list that reshuffles between calls
        // moves the option out from under whoever is clicking.
        var zeta = NewTenant("zeta", "Zeta Flores");
        var aurora = NewTenant("aurora", "Aurora Panadería");
        Given([Accepted(zeta), Accepted(aurora, MemberRole.Agent)], zeta, aurora);

        var result = await GetAsync();

        Assert.Equal(["Aurora Panadería", "Zeta Flores"], result.Memberships.Select(m => m.Name));
        Assert.Equal(MemberRole.Agent, result.Memberships[0].Role);
    }

    [Fact]
    public async Task Only_the_tenants_the_caller_belongs_to_are_asked_for()
    {
        // `tenants` has no Row Level Security of its own, so the ids passed in are the
        // whole access check — they must be exactly the caller's own.
        var tenant = NewTenant("panaderia-aurora", "Panadería Aurora");
        Given([Accepted(tenant)], tenant);

        await GetAsync();

        _tenants.Verify(
            r => r.ListByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(tenant.Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_deactivated_organization_is_not_offered()
    {
        // Listing one that cannot be opened is worse than not listing it.
        var live = NewTenant("aurora", "Aurora");
        var closed = NewTenant("cerrada", "Cerrada");
        closed.Deactivate(_now);
        Given([Accepted(live), Accepted(closed)], live, closed);

        var result = await GetAsync();

        Assert.Equal("Aurora", Assert.Single(result.Memberships).Name);
    }

    [Fact]
    public async Task A_session_whose_user_no_longer_exists_fails_loudly()
    {
        // The token outlived the account. Returning an empty shell would send the client
        // to "create your organization" for a user that cannot own one.
        _users
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
