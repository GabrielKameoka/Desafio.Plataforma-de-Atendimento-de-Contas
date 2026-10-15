using Application.Demo;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class DemoUserRepository(DemoDbContext dbContext) : IDemoUserRepository
{
    public Task<Domain.DemoUser?> FindActiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id && user.IsActive, cancellationToken);
}
