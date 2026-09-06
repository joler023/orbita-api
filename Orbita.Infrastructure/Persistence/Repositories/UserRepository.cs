using Microsoft.EntityFrameworkCore;
using Orbita.Domain.Identity;

namespace Orbita.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(OrbitaDbContext dbContext) : IUserRepository
{
    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
        => dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
        => dbContext.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken)
        => await dbContext.Users.AddAsync(user, cancellationToken);
}
