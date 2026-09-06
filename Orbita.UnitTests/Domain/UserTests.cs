using Orbita.Domain.Identity;

namespace Orbita.UnitTests.Domain;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_NormalizesEmailAndAppliesDefaults()
    {
        var user = User.Create("Owner@Acme.com", "hashed-value", "Jane Doe", Now);

        Assert.Equal("owner@acme.com", user.Email);
        Assert.Equal("hashed-value", user.PasswordHash);
        Assert.Equal("Jane Doe", user.FullName);
        Assert.Null(user.EmailVerifiedAt);
        Assert.Null(user.LastLoginAt);
        Assert.Equal(Now, user.CreatedAt);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_WithInvalidEmail_Throws(string email)
    {
        Assert.Throws<ArgumentException>(() => User.Create(email, "hash", "Jane Doe", Now));
    }

    [Fact]
    public void Create_WithNameTooLong_Throws()
    {
        var tooLong = new string('a', User.FullNameMaxLength + 1);

        Assert.Throws<ArgumentException>(() => User.Create("jane@acme.com", "hash", tooLong, Now));
    }

    [Fact]
    public void RecordLogin_SetsLastLoginAt()
    {
        var user = User.Create("jane@acme.com", "hash", "Jane Doe", Now);
        var loginAt = Now.AddDays(1);

        user.RecordLogin(loginAt);

        Assert.Equal(loginAt, user.LastLoginAt);
    }

    [Fact]
    public void VerifyEmail_SetsEmailVerifiedAt()
    {
        var user = User.Create("jane@acme.com", "hash", "Jane Doe", Now);
        var verifiedAt = Now.AddDays(1);

        user.VerifyEmail(verifiedAt);

        Assert.Equal(verifiedAt, user.EmailVerifiedAt);
    }

    [Fact]
    public void IsLockedOut_WithNoLockout_ReturnsFalse()
    {
        var user = User.Create("jane@acme.com", "hash", "Jane Doe", Now);

        Assert.False(user.IsLockedOut(Now));
    }

    [Fact]
    public void RegisterFailedLogin_BelowThreshold_DoesNotLock()
    {
        var user = User.Create("jane@acme.com", "hash", "Jane Doe", Now);

        user.RegisterFailedLogin(Now, maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));

        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.False(user.IsLockedOut(Now));
    }

    [Fact]
    public void RegisterFailedLogin_AtThreshold_LocksForTheGivenDuration()
    {
        var user = User.Create("jane@acme.com", "hash", "Jane Doe", Now);
        var lockoutDuration = TimeSpan.FromMinutes(15);

        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(Now, maxAttempts: 5, lockoutDuration: lockoutDuration);
        }

        Assert.Equal(5, user.FailedLoginAttempts);
        Assert.True(user.IsLockedOut(Now));
        Assert.False(user.IsLockedOut(Now + lockoutDuration));
    }

    [Fact]
    public void ResetFailedLogins_ClearsCounterAndLockout()
    {
        var user = User.Create("jane@acme.com", "hash", "Jane Doe", Now);
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(Now, maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));
        }

        user.ResetFailedLogins();

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.False(user.IsLockedOut(Now));
    }
}
