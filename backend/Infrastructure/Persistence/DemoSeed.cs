using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public static class DemoSeed
{
    public static async Task ApplyAsync(DemoDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        // Stable identifiers make restarts safe and keep future demo scenarios reproducible.
        var users = new[]
        {
            new DemoUser(Guid.Parse("00000000-0000-0000-0000-000000000101"), "Ana Costa", DemoRole.Attendant),
            new DemoUser(Guid.Parse("00000000-0000-0000-0000-000000000102"), "Bruno Lima", DemoRole.Attendant),
            new DemoUser(Guid.Parse("00000000-0000-0000-0000-000000000103"), "Clara Souza", DemoRole.Supervisor)
        };
        var clients = new[]
        {
            new Client(Guid.Parse("00000000-0000-0000-0000-000000000201"), "Marina Alves"),
            new Client(Guid.Parse("00000000-0000-0000-0000-000000000202"), "Rafael Nunes")
        };

        foreach (var user in users)
        {
            if (!await dbContext.Users.AnyAsync(existing => existing.Id == user.Id, cancellationToken))
                dbContext.Users.Add(user);
        }
        foreach (var client in clients)
        {
            if (!await dbContext.Clients.AnyAsync(existing => existing.Id == client.Id, cancellationToken))
                dbContext.Clients.Add(client);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
