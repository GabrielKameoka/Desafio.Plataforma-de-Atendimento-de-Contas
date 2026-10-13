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
            new DemoUserRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000101"), DisplayName = "Ana Costa", Role = "Attendant" },
            new DemoUserRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000102"), DisplayName = "Bruno Lima", Role = "Attendant" },
            new DemoUserRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000103"), DisplayName = "Clara Souza", Role = "Supervisor" }
        };
        var clients = new[]
        {
            new DemoClientRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000201"), DisplayName = "Marina Alves" },
            new DemoClientRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000202"), DisplayName = "Rafael Nunes" }
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
