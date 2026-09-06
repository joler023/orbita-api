using Moq;
using Orbita.Application.Identity;
using Orbita.Domain.Common;
using Orbita.Domain.Identity;
using Orbita.Domain.Tenants;
using Orbita.UnitTests.TestSupport;

namespace Orbita.UnitTests.Application;

public sealed class TeamInvitationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IMembershipRepository> _memberships = new();
    private readonly Mock<IInvitationTokenRepository> _invitationTokens = new();
    private readonly Mock<ITenantContextSetter> _tenantContextSetter = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IInvitationEmailSender> _emailSender = new();
    private readonly TeamInvitationService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();

    public TeamInvitationServiceTests()
    {
        // QueryInTenantScopeAsync just runs the query directly — the transaction/RLS
        // synchronization it wraps in production is Infrastructure's concern, not
        // something these orchestration tests need to exercise.
        _unitOfWork
            .Setup(u => u.QueryInTenantScopeAsync(It.IsAny<Func<CancellationToken, Task<Membership?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Membership?>> query, CancellationToken ct) => query(ct));

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tenant.Create("acme", "Acme Corp", now: Now));

        _sut = new TeamInvitationService(
            _tenants.Object,
            _users.Object,
            _memberships.Object,
            _invitationTokens.Object,
            _tenantContextSetter.Object,
            _unitOfWork.Object,
            _passwordHasher.Object,
            _emailSender.Object,
            new FixedTimeProvider(Now));
    }

    private Membership OwnerMembership() => Membership.CreateOwner(_tenantId, _ownerId, Now);

    [Fact]
    public async Task InviteAsync_WhenCallerIsOwner_CreatesInvitedUserAndPendingMembership()
    {
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnerMembership());
        _users.Setup(r => r.GetByEmailAsync("newperson@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("placeholder-hash");

        var summary = await _sut.InviteAsync(_tenantId, _ownerId, new InviteTeamMemberRequest("newperson@acme.com", MemberRole.Agent), CancellationToken.None);

        Assert.Equal("newperson@acme.com", summary.Email);
        Assert.Equal(MemberRole.Agent, summary.Role);
        _users.Verify(r => r.AddAsync(It.Is<User>(u => u.Email == "newperson@acme.com" && u.PasswordSetAt == null), It.IsAny<CancellationToken>()), Times.Once);
        _memberships.Verify(r => r.AddAsync(It.Is<Membership>(m => m.TenantId == _tenantId && m.Role == MemberRole.Agent && m.IsPending), It.IsAny<CancellationToken>()), Times.Once);
        _invitationTokens.Verify(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(s => s.SendAsync("newperson@acme.com", "Acme Corp", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InviteAsync_WhenCallerHasNoMembership_ThrowsForbidden()
    {
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>())).ReturnsAsync((Membership?)null);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.InviteAsync(_tenantId, _ownerId, new InviteTeamMemberRequest("x@acme.com", MemberRole.Agent), CancellationToken.None));

        _memberships.Verify(r => r.AddAsync(It.IsAny<Membership>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(MemberRole.Agent)]
    [InlineData(MemberRole.Viewer)]
    public async Task InviteAsync_WhenCallerIsNotOwnerOrAdmin_ThrowsForbidden(MemberRole callerRole)
    {
        var caller = Membership.Invite(_tenantId, _ownerId, callerRole, Guid.NewGuid(), Now);
        caller.Accept(Now);
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.InviteAsync(_tenantId, _ownerId, new InviteTeamMemberRequest("x@acme.com", MemberRole.Agent), CancellationToken.None));
    }

    [Fact]
    public async Task InviteAsync_WhenCallerHasNotAcceptedTheirOwnInvite_ThrowsForbidden()
    {
        var caller = Membership.Invite(_tenantId, _ownerId, MemberRole.Admin, Guid.NewGuid(), Now);
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.InviteAsync(_tenantId, _ownerId, new InviteTeamMemberRequest("x@acme.com", MemberRole.Agent), CancellationToken.None));
    }

    [Fact]
    public async Task InviteAsync_WhenEmailAlreadyHasAMembership_ThrowsConflict()
    {
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(OwnerMembership());
        var existingUser = User.Create("existing@acme.com", "hash", "Existing Person", Now);
        _users.Setup(r => r.GetByEmailAsync("existing@acme.com", It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, existingUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.CreateOwner(_tenantId, existingUser.Id, Now));

        await Assert.ThrowsAsync<MembershipAlreadyExistsException>(
            () => _sut.InviteAsync(_tenantId, _ownerId, new InviteTeamMemberRequest("existing@acme.com", MemberRole.Agent), CancellationToken.None));

        _memberships.Verify(r => r.AddAsync(It.IsAny<Membership>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResendAsync_WithPendingInvitation_InvalidatesOldTokenAndIssuesNew()
    {
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(OwnerMembership());
        var membership = Membership.Invite(_tenantId, Guid.NewGuid(), MemberRole.Agent, _ownerId, Now);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        var invitedUser = User.CreateInvited("invitee@acme.com", "invitee", "placeholder", Now);
        _users.Setup(r => r.GetByIdAsync(membership.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(invitedUser);

        var summary = await _sut.ResendAsync(_tenantId, _ownerId, membership.Id, CancellationToken.None);

        Assert.Equal("invitee@acme.com", summary.Email);
        _invitationTokens.Verify(r => r.InvalidateForMembershipAsync(membership.Id, Now, It.IsAny<CancellationToken>()), Times.Once);
        _invitationTokens.Verify(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(s => s.SendAsync("invitee@acme.com", "Acme Corp", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResendAsync_WhenInvitationIsAlreadyAccepted_ThrowsNotFound()
    {
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(OwnerMembership());
        var membership = Membership.Invite(_tenantId, Guid.NewGuid(), MemberRole.Agent, _ownerId, Now);
        membership.Accept(Now);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);

        await Assert.ThrowsAsync<InvitationNotFoundException>(() => _sut.ResendAsync(_tenantId, _ownerId, membership.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RevokeAsync_WithPendingInvitation_DeactivatesItAndInvalidatesTokens()
    {
        _memberships.Setup(r => r.GetByTenantAndUserAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(OwnerMembership());
        var membership = Membership.Invite(_tenantId, Guid.NewGuid(), MemberRole.Agent, _ownerId, Now);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);

        await _sut.RevokeAsync(_tenantId, _ownerId, membership.Id, CancellationToken.None);

        Assert.False(membership.IsActive);
        _invitationTokens.Verify(r => r.InvalidateForMembershipAsync(membership.Id, Now, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptAsync_WithUnknownToken_ThrowsInvalidInvitation()
    {
        _invitationTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((InvitationToken?)null);

        await Assert.ThrowsAsync<InvalidInvitationException>(
            () => _sut.AcceptAsync(new AcceptInvitationRequest("bad-token", null, "correct-horse-battery"), CancellationToken.None));
    }

    [Fact]
    public async Task AcceptAsync_WithExpiredToken_ThrowsInvalidInvitation()
    {
        var expired = InvitationToken.Issue(Guid.NewGuid(), _tenantId, "hash", Now.AddDays(-8), TimeSpan.FromDays(7));
        _invitationTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(expired);

        await Assert.ThrowsAsync<InvalidInvitationException>(
            () => _sut.AcceptAsync(new AcceptInvitationRequest("token", null, "correct-horse-battery"), CancellationToken.None));
    }

    [Fact]
    public async Task AcceptAsync_ForAPlaceholderUser_SetsPasswordAndAcceptsMembership()
    {
        var membership = Membership.Invite(_tenantId, Guid.NewGuid(), MemberRole.Agent, _ownerId, Now);
        var invitationToken = InvitationToken.Issue(membership.Id, _tenantId, "hash", Now, TimeSpan.FromDays(7));
        var invitedUser = User.CreateInvited("invitee@acme.com", "invitee", "placeholder-hash", Now);

        _invitationTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(invitationToken);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        _users.Setup(r => r.GetByIdAsync(membership.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(invitedUser);
        _passwordHasher.Setup(h => h.Hash("correct-horse-battery")).Returns("real-hash");

        var result = await _sut.AcceptAsync(new AcceptInvitationRequest("raw-token", "Real Name", "correct-horse-battery"), CancellationToken.None);

        Assert.Equal(invitedUser.Id, result.UserId);
        Assert.Equal("real-hash", invitedUser.PasswordHash);
        Assert.NotNull(invitedUser.PasswordSetAt);
        Assert.Equal("Real Name", invitedUser.FullName);
        Assert.NotNull(invitedUser.EmailVerifiedAt);
        Assert.True(membership.AcceptedAt is not null);
        Assert.NotNull(invitationToken.UsedAt);
        _passwordHasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AcceptAsync_ForAnExistingAccount_VerifiesThePasswordInsteadOfReplacingIt()
    {
        var membership = Membership.Invite(_tenantId, Guid.NewGuid(), MemberRole.Agent, _ownerId, Now);
        var invitationToken = InvitationToken.Issue(membership.Id, _tenantId, "hash", Now, TimeSpan.FromDays(7));
        var existingUser = User.Create("existing@acme.com", "existing-hash", "Existing Person", Now);

        _invitationTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(invitationToken);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        _users.Setup(r => r.GetByIdAsync(membership.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
        _passwordHasher.Setup(h => h.Verify("existing-hash", "correct-horse-battery")).Returns(true);

        var result = await _sut.AcceptAsync(new AcceptInvitationRequest("raw-token", null, "correct-horse-battery"), CancellationToken.None);

        Assert.Equal(existingUser.Id, result.UserId);
        Assert.Equal("existing-hash", existingUser.PasswordHash);
        Assert.True(membership.AcceptedAt is not null);
    }

    [Fact]
    public async Task AcceptAsync_ForAnExistingAccountWithWrongPassword_ThrowsInvalidCredentials()
    {
        var membership = Membership.Invite(_tenantId, Guid.NewGuid(), MemberRole.Agent, _ownerId, Now);
        var invitationToken = InvitationToken.Issue(membership.Id, _tenantId, "hash", Now, TimeSpan.FromDays(7));
        var existingUser = User.Create("existing@acme.com", "existing-hash", "Existing Person", Now);

        _invitationTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(invitationToken);
        _memberships.Setup(r => r.GetByIdAsync(membership.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        _users.Setup(r => r.GetByIdAsync(membership.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(existingUser);
        _passwordHasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _sut.AcceptAsync(new AcceptInvitationRequest("raw-token", null, "wrong-password"), CancellationToken.None));

        Assert.Null(membership.AcceptedAt);
    }
}
