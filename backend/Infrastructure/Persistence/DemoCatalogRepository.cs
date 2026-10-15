using Application.Demo;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class DemoCatalogRepository(DemoDbContext dbContext) : IDemoCatalog
{
    public async Task<IReadOnlyList<Application.Demo.DemoUser>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Users.AsNoTracking()
            .Where(user => user.IsActive)
            .OrderBy(user => user.DisplayName)
            .Select(user => new Application.Demo.DemoUser(user.Id, user.DisplayName, user.Role.ToString()))
            .ToListAsync(cancellationToken);

    public async Task<DemoCatalog> GetAsync(CancellationToken cancellationToken = default)
    {
        var users = await GetUsersAsync(cancellationToken);
        var clients = await dbContext.Clients.AsNoTracking()
            .OrderBy(client => client.DisplayName)
            .Select(client => new DemoClient(client.Id, client.DisplayName))
            .ToListAsync(cancellationToken);
        return new DemoCatalog(users, clients);
    }
}
