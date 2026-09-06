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
}
