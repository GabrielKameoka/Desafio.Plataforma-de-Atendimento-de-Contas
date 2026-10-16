using System.Text.Json;
using Api.Authentication;
using Application.Requests;
using Domain;
using Microsoft.AspNetCore.Antiforgery;

namespace Api.Endpoints;

public static class RequestHandlingEndpoints
{
    public static IEndpointRouteBuilder MapRequestHandlingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var requests = endpoints.MapGroup("/api/requests").RequireAuthorization(DemoAuth.StaffPolicy);
        requests.MapGet("/{id:guid}", async (Guid id, GetRequestDetailUseCase get, CancellationToken cancellationToken) =>
            await get.ExecuteAsync(id, cancellationToken) is { } detail
                ? Results.Ok(detail)
                : ApiProblems.NotFound("request_not_found", "Solicitação não encontrada."));
        requests.MapPost("/{id:guid}/interactions", (Guid id, HttpContext context, IAntiforgery antiforgery,
            HandleRequestUseCase handle, CancellationToken ct) => MutateAsync(id, context, antiforgery, handle,
                (body, actor) => handle.AddInteractionAsync(id, body.Version, actor, body.Text, ct), ct))
            .RequireAuthorization(DemoAuth.AttendantPolicy);
        requests.MapPost("/{id:guid}/status", (Guid id, HttpContext context, IAntiforgery antiforgery,
            HandleRequestUseCase handle, CancellationToken ct) => MutateAsync(id, context, antiforgery, handle,
                (body, actor) => Enum.TryParse<RequestStatus>(body.Status, true, out var status) && Enum.IsDefined(status)
                    ? handle.ChangeStatusAsync(id, body.Version, actor, status, body.Reason, ct)
                    : throw new DomainRuleException("invalid_status", "Informe um status válido."), ct))
            .RequireAuthorization(DemoAuth.AttendantPolicy);
        requests.MapPost("/{id:guid}/assignment", (Guid id, HttpContext context, IAntiforgery antiforgery,
            HandleRequestUseCase handle, CancellationToken ct) => MutateAsync(id, context, antiforgery, handle,
                (body, actor) => handle.AssignAsync(id, body.Version, actor, body.AssigneeId, body.Reason, ct), ct))
            .RequireAuthorization(DemoAuth.AttendantPolicy);
        requests.MapPost("/{id:guid}/priority", (Guid id, HttpContext context, IAntiforgery antiforgery,
            HandleRequestUseCase handle, CancellationToken ct) => MutateAsync(id, context, antiforgery, handle,
                (body, actor) => Enum.TryParse<RequestPriority>(body.Priority, true, out var priority) && Enum.IsDefined(priority)
                    ? handle.ChangePriorityAsync(id, body.Version, actor, priority, body.Reason, ct)
                    : throw new DomainRuleException("invalid_priority", "Informe uma prioridade válida."), ct))
            .RequireAuthorization(DemoAuth.AttendantPolicy);
        requests.MapPost("/{id:guid}/deadline", (Guid id, HttpContext context, IAntiforgery antiforgery,
            HandleRequestUseCase handle, CancellationToken ct) => MutateAsync(id, context, antiforgery, handle,
                (body, actor) => handle.ChangeDeadlineAsync(id, body.Version, actor,
                    body.DeadlineAtUtc ?? default, body.Reason, ct), ct))
            .RequireAuthorization(DemoAuth.AttendantPolicy);
        requests.MapPost("/{id:guid}/reopen", (Guid id, HttpContext context, IAntiforgery antiforgery,
            HandleRequestUseCase handle, CancellationToken ct) => MutateAsync(id, context, antiforgery, handle,
                (body, actor) => handle.ReopenAsync(id, body.Version, actor, body.Reason, ct), ct))
            .RequireAuthorization(DemoAuth.SupervisorPolicy);
        return endpoints;
    }

    private static async Task<IResult> MutateAsync(Guid id, HttpContext context, IAntiforgery antiforgery,
        HandleRequestUseCase handle, Func<ChangeRequestBody, Guid, Task<RequestDetail>> mutate,
        CancellationToken cancellationToken)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return ApiProblems.BadRequest("invalid_csrf", "Token de proteção inválido.");
        }
        if (!context.Request.HasJsonContentType())
            return ApiProblems.BadRequest("invalid_body", "Envie JSON com a versão exibida do caso.");
        ChangeRequestBody? body;
        try
        {
            body = await context.Request.ReadFromJsonAsync<ChangeRequestBody>(cancellationToken);
        }
        catch (JsonException)
        {
            return ApiProblems.BadRequest("invalid_body", "Corpo JSON inválido.");
        }
        if (body is null)
            return ApiProblems.BadRequest("invalid_body", "Informe os dados da alteração.");
        var session = DemoAuth.CurrentSession(context.User);
        if (session is null)
            return ApiProblems.Unauthorized();
        try
        {
            return Results.Ok(await mutate(body, session.Id));
        }
        catch (DomainRuleException error)
        {
            return ApiProblems.BadRequest(error.Code, error.Message);
        }
        catch (RequestNotFoundException)
        {
            return ApiProblems.NotFound("request_not_found", "Solicitação não encontrada.");
        }
        catch (AttendantUnavailableException)
        {
            return ApiProblems.Forbidden();
        }
        catch (SupervisorUnavailableException)
        {
            return ApiProblems.Forbidden();
        }
        catch (RequestVersionConflictException error)
        {
            return Results.Problem(title: "O caso foi alterado por outra pessoa. Leia o estado atual antes de decidir novamente.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "version_conflict",
                    ["requestId"] = error.RequestId,
                    ["currentVersion"] = error.CurrentVersion
                });
        }
    }
}

public sealed record ChangeRequestBody(long Version, string? Text = null, string? Status = null,
    string? Priority = null, Guid AssigneeId = default, DateTimeOffset? DeadlineAtUtc = null,
    string? Reason = null);
