using Api.Authentication;
using Application.Clients;
using Domain;

namespace Api.Endpoints;

public static class ClientEndpoints
{
    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/clients").RequireAuthorization(DemoAuth.StaffPolicy);

        group.MapGet("/", async (string? query, SearchClientsUseCase search,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await search.ExecuteAsync(query, cancellationToken));
            }
            catch (DomainRuleException exception)
            {
                return ApiProblems.BadRequest(exception.Code, exception.Message);
            }
        });

        group.MapGet("/{id}", async (string id, GetClientContextUseCase get,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(id, out var clientId))
                return ApiProblems.BadRequest("invalid_client_id", "Informe um identificador de cliente válido.");

            try
            {
                var context = await get.ExecuteAsync(clientId, cancellationToken);
                return context is null
                    ? ApiProblems.NotFound("client_not_found", "Cliente não encontrado.")
                    : Results.Ok(context);
            }
            catch (DomainRuleException exception)
            {
                return ApiProblems.BadRequest(exception.Code, exception.Message);
            }
        });

        return endpoints;
    }
}
