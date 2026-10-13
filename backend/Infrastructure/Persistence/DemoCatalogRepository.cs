using Application.Demo;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class DemoCatalogRepository(DemoDbContext dbContext) : IDemoCatalog
{
    public async Task<DemoCatalog> GetAsync(CancellationToken cancellationToken = default)
    {
        var users = await dbContext.Users.AsNoTracking()
            .OrderBy(user => user.DisplayName)
            .Select(user => new DemoUser(user.Id, user.DisplayName, user.Role))
            .ToListAsync(cancellationToken);
        var clients = await dbContext.Clients.AsNoTracking()
            .OrderBy(client => client.DisplayName)
            .Select(client => new DemoClient(client.Id, client.DisplayName))
            .ToListAsync(cancellationToken);
        return new DemoCatalog(users, clients);
    }
}
