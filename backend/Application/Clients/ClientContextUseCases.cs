using Domain;

namespace Application.Clients;

public sealed record ClientSearchItem(Guid Id, string DisplayName);

public sealed record ClientRequestData(Guid Id, Guid ClientId, string Protocol, string Type,
    string Description, string Priority, string Status, Guid AssigneeId, string AssigneeName,
    DateTimeOffset OpenedAtUtc, DateTimeOffset DeadlineAtUtc, long Version);

public sealed record ClientInteractionData(Guid Id, Guid RequestId, Guid AuthorId,
    string AuthorName, string Text, DateTimeOffset CreatedAtUtc);

public sealed record ClientContextData(ClientSearchItem Client,
    IReadOnlyList<ClientRequestData> Requests, IReadOnlyList<ClientInteractionData> Interactions);

public sealed record ClientRequestSummary(Guid Id, string Protocol, string Type, string Description,
    string Priority, string Status, Guid AssigneeId, string AssigneeName,
    DateTimeOffset OpenedAtUtc, DateTimeOffset DeadlineAtUtc, long Version);

public sealed record ClientInteractionSummary(Guid Id, Guid RequestId, string Protocol,
    Guid AuthorId, string AuthorName, string Text, DateTimeOffset CreatedAtUtc);

public sealed record ClientContext(Guid Id, string DisplayName,
    IReadOnlyList<ClientRequestSummary> Requests, IReadOnlyList<ClientInteractionSummary> Interactions);

public interface IClientContextRepository
{
    Task<IReadOnlyList<ClientSearchItem>> SearchAsync(string query, int limit,
        CancellationToken cancellationToken = default);

    Task<ClientContextData?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class SearchClientsUseCase(IClientContextRepository repository)
{
    public async Task<IReadOnlyList<ClientSearchItem>> ExecuteAsync(string? query,
        CancellationToken cancellationToken = default)
    {
        var normalized = string.Join(' ', (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length is < 1 or > 100)
            throw new DomainRuleException("invalid_client_query", "Informe um nome ou identificador de cliente com até 100 caracteres.");

        return await repository.SearchAsync(normalized, 20, cancellationToken);
    }
}

public sealed class GetClientContextUseCase(IClientContextRepository repository)
{
    public async Task<ClientContext?> ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new DomainRuleException("invalid_client_id", "Informe um identificador de cliente válido.");

        var data = await repository.FindAsync(id, cancellationToken);
        if (data is null || data.Client.Id != id)
            return null;

        var requests = data.Requests
            .Where(request => request.ClientId == id)
            .OrderBy(request => request.OpenedAtUtc)
            .ThenBy(request => request.Id)
            .ToArray();
        var requestsById = requests.ToDictionary(request => request.Id);
        var interactions = data.Interactions
            .Where(interaction => requestsById.ContainsKey(interaction.RequestId))
            .OrderBy(interaction => interaction.CreatedAtUtc)
            .ThenBy(interaction => interaction.Id)
            .Select(interaction => new ClientInteractionSummary(interaction.Id, interaction.RequestId,
                requestsById[interaction.RequestId].Protocol, interaction.AuthorId,
                interaction.AuthorName, interaction.Text, interaction.CreatedAtUtc))
            .ToArray();

        return new ClientContext(data.Client.Id, data.Client.DisplayName,
            requests.Select(request => new ClientRequestSummary(request.Id, request.Protocol,
                request.Type, request.Description, request.Priority, request.Status,
                request.AssigneeId, request.AssigneeName, request.OpenedAtUtc,
                request.DeadlineAtUtc, request.Version)).ToArray(), interactions);
    }
}
