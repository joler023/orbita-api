using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Tenants;

namespace Orbita.Infrastructure.Persistence;

public sealed class OrbitaDbContext(DbContextOptions<OrbitaDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrbitaDbContext).Assembly);
    }
}
