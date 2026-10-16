using System.Text.Json;
using Api.Authentication;
using Application.Requests;
using Domain;
using Microsoft.AspNetCore.Antiforgery;

namespace Api.Endpoints;

public static class RequestEndpoints
{
    public static IEndpointRouteBuilder MapRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/requests", OpenAsync)
            .RequireAuthorization(DemoAuth.AttendantPolicy);
        return endpoints;
    }

    private static async Task<IResult> OpenAsync(HttpContext context, IAntiforgery antiforgery,
        OpenFeeDisputeUseCase open, CancellationToken cancellationToken)
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
            return ApiProblems.BadRequest("invalid_body", "Envie JSON com cliente e descrição.");
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1)
            return ApiProblems.BadRequest("idempotency_key_required", "Informe uma chave de idempotência.");

        OpenRequestBody? body;
        try
        {
            body = await context.Request.ReadFromJsonAsync<OpenRequestBody>(cancellationToken);
        }
        catch (JsonException)
        {
            return ApiProblems.BadRequest("invalid_body", "Corpo JSON inválido.");
        }
        if (body is null)
            return ApiProblems.BadRequest("invalid_body", "Cliente e descrição são obrigatórios.");

        var session = DemoAuth.CurrentSession(context.User);
        if (session is null)
            return ApiProblems.Unauthorized();

        try
        {
            var result = await open.ExecuteAsync(body.ClientId, session.Id,
                body.Description, keys[0], cancellationToken);
            return Results.Json(result.Response, statusCode: result.WasCreated
                ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        }
        catch (DomainRuleException exception)
        {
            return ApiProblems.BadRequest(exception.Code, exception.Message);
        }
        catch (RequestClientNotFoundException)
        {
            return ApiProblems.NotFound("client_not_found", "Cliente não encontrado.");
        }
        catch (AttendantUnavailableException)
        {
            return ApiProblems.Forbidden();
        }
        catch (IdempotencyConflictException)
        {
            return ApiProblems.Conflict("idempotency_conflict", "Esta chave já foi usada para outra abertura.");
        }
    }
}

public sealed record OpenRequestBody(Guid ClientId, string? Description);
