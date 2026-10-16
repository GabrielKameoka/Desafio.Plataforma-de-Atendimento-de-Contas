using Application.Clients;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class ClientContextRepository(DemoDbContext dbContext) : IClientContextRepository
{
    public async Task<IReadOnlyList<ClientSearchItem>> SearchAsync(string query, int limit,
        CancellationToken cancellationToken = default)
    {
        var name = query.ToLowerInvariant();
        var isId = Guid.TryParse(query, out var id);

        return await dbContext.Clients.AsNoTracking()
            .Where(client => client.DisplayName.ToLower().Contains(name) || (isId && client.Id == id))
            .OrderBy(client => client.DisplayName)
            .ThenBy(client => client.Id)
            .Take(limit)
            .Select(client => new ClientSearchItem(client.Id, client.DisplayName))
            .ToListAsync(cancellationToken);
    }

    public async Task<ClientContextData?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var client = await dbContext.Clients.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new ClientSearchItem(item.Id, item.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);
        if (client is null)
            return null;

        var requests = await dbContext.Requests.AsNoTracking()
            .Where(request => request.ClientId == id)
            .OrderBy(request => request.OpenedAtUtc)
            .ThenBy(request => request.Id)
            .Select(request => new ClientRequestData(request.Id, request.ClientId, request.Protocol,
                request.Type.ToString(), request.Description, request.Priority.ToString(),
                request.Status.ToString(), request.AssigneeId, request.Assignee.DisplayName,
                request.OpenedAtUtc, request.DeadlineAtUtc, request.Version))
            .ToListAsync(cancellationToken);

        var interactions = await dbContext.Interactions.AsNoTracking()
            .Where(interaction => interaction.Request.ClientId == id)
            .OrderBy(interaction => interaction.CreatedAtUtc)
            .ThenBy(interaction => interaction.Id)
            .Select(interaction => new ClientInteractionData(interaction.Id, interaction.RequestId,
                interaction.AuthorId, interaction.Author.DisplayName, interaction.Text,
                interaction.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new ClientContextData(client, requests, interactions);
    }
}
