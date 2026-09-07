using Orbita.Domain.Tenants;

namespace Orbita.UnitTests.Domain;

public sealed class TenantTests
{
    [Fact]
    public void Create_WithValidData_NormalizesSlugAndAppliesDefaults()
    {
        var tenant = Tenant.Create("Acme-Corp", "Acme Corp");

        Assert.Equal("acme-corp", tenant.Slug);
        Assert.Equal("Acme Corp", tenant.Name);
        Assert.Equal("CO", tenant.CountryCode);
        Assert.Equal("America/Bogota", tenant.Timezone);
        Assert.Equal("es-CO", tenant.Locale);
        Assert.True(tenant.IsActive);
        Assert.False(tenant.RequireMfaForMembers);
        Assert.Equal(tenant.CreatedAt, tenant.UpdatedAt);
    }

    [Fact]
    public void SetRequireMfaForMembers_UpdatesFlagAndTimestamp()
    {
        var tenant = Tenant.Create("acme", "Acme", now: DateTimeOffset.UnixEpoch);
        var later = DateTimeOffset.UnixEpoch.AddDays(1);

        tenant.SetRequireMfaForMembers(true, later);

        Assert.True(tenant.RequireMfaForMembers);
        Assert.Equal(later, tenant.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_WithEmptySlug_Throws(string slug)
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create(slug, "Acme Corp"));
    }

    [Fact]
    public void Create_WithInvalidSlugCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create("acme corp!", "Acme Corp"));
    }

    [Fact]
    public void Create_WithNameTooLong_Throws()
    {
        var tooLong = new string('a', Tenant.NameMaxLength + 1);

        Assert.Throws<ArgumentException>(() => Tenant.Create("acme", tooLong));
    }

    [Fact]
    public void Rename_UpdatesNameAndTimestamp()
    {
        var tenant = Tenant.Create("acme", "Acme", now: DateTimeOffset.UnixEpoch);
        var later = DateTimeOffset.UnixEpoch.AddDays(1);

        tenant.Rename("Acme Inc", later);

        Assert.Equal("Acme Inc", tenant.Name);
        Assert.Equal(later, tenant.UpdatedAt);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalseAndBumpsTimestamp()
    {
        var tenant = Tenant.Create("acme", "Acme", now: DateTimeOffset.UnixEpoch);
        var later = DateTimeOffset.UnixEpoch.AddDays(1);

        tenant.Deactivate(later);

        Assert.False(tenant.IsActive);
        Assert.Equal(later, tenant.UpdatedAt);
    }

    [Fact]
    public void Activate_SetsIsActiveTrueAndBumpsTimestamp()
    {
        var tenant = Tenant.Create("acme", "Acme", now: DateTimeOffset.UnixEpoch);
        var deactivatedAt = DateTimeOffset.UnixEpoch.AddDays(1);
        var reactivatedAt = DateTimeOffset.UnixEpoch.AddDays(2);
        tenant.Deactivate(deactivatedAt);

        tenant.Activate(reactivatedAt);

        Assert.True(tenant.IsActive);
        Assert.Equal(reactivatedAt, tenant.UpdatedAt);
    }

    [Theory]
    [InlineData("Acme Corp", "acme-corp")]
    [InlineData("  Café Bogotá S.A.S.  ", "cafe-bogota-s-a-s")]
    [InlineData("¡Panadería El Sol!", "panaderia-el-sol")]
    [InlineData("---", "org")]
    [InlineData("", "org")]
    public void Slugify_SanitizesArbitraryBusinessNames(string name, string expectedSlug)
    {
        Assert.Equal(expectedSlug, Tenant.Slugify(name));
    }

    [Fact]
    public void Slugify_TruncatesToSlugMaxLength()
    {
        var longName = new string('a', Tenant.SlugMaxLength + 20);

        var slug = Tenant.Slugify(longName);

        Assert.True(slug.Length <= Tenant.SlugMaxLength);
    }

    [Fact]
    public void Slugify_ProducesSlugAcceptedByCreate()
    {
        var slug = Tenant.Slugify("Café Bogotá S.A.S.");

        var tenant = Tenant.Create(slug, "Café Bogotá S.A.S.");

        Assert.Equal(slug, tenant.Slug);
    }
}
