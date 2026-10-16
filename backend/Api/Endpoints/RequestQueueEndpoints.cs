using Api.Authentication;
using Application.Requests;
using Domain;

namespace Api.Endpoints;

public static class RequestQueueEndpoints
{
    public static IEndpointRouteBuilder MapRequestQueueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/requests", SearchAsync)
            .RequireAuthorization(DemoAuth.SupervisorPolicy);
        return endpoints;
    }

    private static async Task<IResult> SearchAsync(HttpContext context, GetRequestQueueUseCase search,
        CancellationToken cancellationToken)
    {
        var input = context.Request.Query;
        if (!TryEnum<RequestStatus>(input["status"], out var status) ||
            !TryEnum<RequestPriority>(input["priority"], out var priority) ||
            !TryEnum<DueFilter>(input["due"], out var due))
            return ApiProblems.BadRequest("invalid_request_filter", "Status, prioridade ou prazo inválido.");
        Guid? assigneeId = null;
        if (input.TryGetValue("assigneeId", out var assigneeRaw))
        {
            if (assigneeRaw.Count != 1 || !Guid.TryParse(assigneeRaw[0], out var parsed) || parsed == Guid.Empty)
                return ApiProblems.BadRequest("invalid_request_filter", "Responsável inválido.");
            assigneeId = parsed;
        }
        if (!TryInt(input["page"], 1, out var page) || !TryInt(input["pageSize"], 20, out var pageSize))
            return ApiProblems.BadRequest("invalid_request_filter", "Paginação inválida.");
        try
        {
            return Results.Ok(await search.ExecuteAsync(new RequestQueueQuery(status, priority,
                assigneeId, due, page, pageSize), cancellationToken));
        }
        catch (DomainRuleException error)
        {
            return ApiProblems.BadRequest(error.Code, error.Message);
        }
    }

    private static bool TryEnum<T>(Microsoft.Extensions.Primitives.StringValues raw, out T? value)
        where T : struct, Enum
    {
        value = null;
        if (raw.Count == 0)
            return true;
        if (raw.Count != 1 || string.IsNullOrWhiteSpace(raw[0]) ||
            !Enum.TryParse<T>(raw[0], true, out var parsed) || !Enum.IsDefined(parsed))
            return false;
        value = parsed;
        return true;
    }

    private static bool TryInt(Microsoft.Extensions.Primitives.StringValues raw, int fallback, out int value)
    {
        value = fallback;
        return raw.Count == 0 || raw.Count == 1 && int.TryParse(raw[0], out value);
    }
}
