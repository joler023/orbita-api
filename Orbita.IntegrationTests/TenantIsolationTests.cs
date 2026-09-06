using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Identity;
using Orbita.Infrastructure.Persistence;

namespace Orbita.IntegrationTests;

/// <summary>
/// ORB-A09: "no endpoint of one tenant returns data belonging to another" — the
/// highest-consequence acceptance criterion in the whole backlog. Exercises both
/// isolation layers independently: the EF Core global query filter, and Postgres Row
/// Level Security itself with the filter deliberately switched off.
/// </summary>
public sealed class TenantIsolationTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;
    private readonly HttpClient _client;

    public TenantIsolationTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task QueryFilter_OnlyReturnsMembershipsForTheAmbientTenant()
    {
        var tenantA = await RegisterAsync("Filter Isolation A");
        await RegisterAsync("Filter Isolation B");

        // Connects as the admin/table-owner role, which Postgres exempts from Row
        // Level Security entirely — the only way to observe the EF Core query filter
        // (layer 1) on its own, since every request-scoped OrbitaDbContext connects as
        // orbita_app and would have RLS (layer 2) applying at the same time.
        var tenantContext = new AmbientTenantContext();
        tenantContext.SetTenant(tenantA.TenantId);
        var adminOptions = new DbContextOptionsBuilder<OrbitaDbContext>()
            .UseNpgsql(_fixture.GetAdminConnectionString())
            .Options;
        await using var dbContext = new OrbitaDbContext(adminOptions, tenantContext);

        var visible = await dbContext.Memberships.ToListAsync();

        var membership = Assert.Single(visible);
        Assert.Equal(tenantA.TenantId, membership.TenantId);
    }

    [Fact]
    public async Task RowLevelSecurity_BlocksCrossTenantReadsEvenWithQueryFilterDisabled()
    {
        var tenantA = await RegisterAsync("RLS Isolation A");
        await RegisterAsync("RLS Isolation B");

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();

        // Same session variable UnitOfWork sets before a real write, applied here
        // directly so the read below proves the database — not the C# filter — is
        // what enforces isolation.
        await SetSessionTenantAsync(dbContext, tenantA.TenantId.ToString());

        var visible = await dbContext.Memberships.IgnoreQueryFilters().ToListAsync();

        var membership = Assert.Single(visible);
        Assert.Equal(tenantA.TenantId, membership.TenantId);
    }

    [Fact]
    public async Task RowLevelSecurity_DeniesEveryRowWhenNoTenantSessionIsSet()
    {
        await RegisterAsync("RLS No Session");

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        await SetSessionTenantAsync(dbContext, tenantId: string.Empty);

        var visible = await dbContext.Memberships.IgnoreQueryFilters().ToListAsync();

        Assert.Empty(visible);
    }

    private static async Task SetSessionTenantAsync(OrbitaDbContext dbContext, string tenantId)
    {
        // Pins one physical connection for the rest of this dbContext's lifetime, so
        // the session value set here is guaranteed to still be there for the query
        // that follows — see the same concern in Orbita.Infrastructure.Persistence.UnitOfWork.
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {tenantId}, false);");
    }

    private async Task<RegisterOrganizationResult> RegisterAsync(string businessName)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/organizations",
            new RegisterOrganizationRequest(businessName, "Jane Doe", $"{Guid.NewGuid():N}@acme.com", "correct-horse-battery"));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterOrganizationResult>())!;
    }
}
