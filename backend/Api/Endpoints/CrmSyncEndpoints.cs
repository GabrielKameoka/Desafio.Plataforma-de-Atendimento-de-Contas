using Api.Authentication;
using Application.CrmSync;
using Application.Requests;
using Microsoft.AspNetCore.Antiforgery;

namespace Api.Endpoints;

public static class CrmSyncEndpoints
{
    public static IEndpointRouteBuilder MapCrmSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/requests/{id:guid}/sync", async (Guid id,
            GetRequestSyncUseCase get, CancellationToken cancellationToken) =>
            await get.ExecuteAsync(id, cancellationToken) is { } state
                ? Results.Ok(state)
                : ApiProblems.NotFound("request_not_found", "Solicitação não encontrada."))
            .RequireAuthorization(DemoAuth.StaffPolicy);
        endpoints.MapPost("/api/requests/{id:guid}/sync/retry", RetryAsync)
            .RequireAuthorization(DemoAuth.SupervisorPolicy);
        endpoints.MapGet("/api/demo/crm", GetAvailabilityAsync)
            .RequireAuthorization(DemoAuth.SupervisorPolicy);
        endpoints.MapPost("/api/demo/crm/unavailable", (HttpContext context, IAntiforgery antiforgery,
            ManageCrmDemoUseCase manage, CancellationToken ct) =>
            SetAvailabilityAsync(false, context, antiforgery, manage, ct))
            .RequireAuthorization(DemoAuth.SupervisorPolicy);
        endpoints.MapPost("/api/demo/crm/available", (HttpContext context, IAntiforgery antiforgery,
            ManageCrmDemoUseCase manage, CancellationToken ct) =>
            SetAvailabilityAsync(true, context, antiforgery, manage, ct))
            .RequireAuthorization(DemoAuth.SupervisorPolicy);
        return endpoints;
    }

    private static async Task<IResult> RetryAsync(Guid id, HttpContext context,
        IAntiforgery antiforgery, RetryRequestSyncUseCase retry, CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfAsync(context, antiforgery))
            return ApiProblems.BadRequest("invalid_csrf", "Token de proteção inválido.");
        try
        {
            return Results.Ok(await retry.ExecuteAsync(id, cancellationToken));
        }
        catch (RequestNotFoundException)
        {
            return ApiProblems.NotFound("request_not_found", "Solicitação não encontrada.");
        }
        catch (NoFailedSyncException)
        {
            return ApiProblems.BadRequest("no_failed_sync", "Não há envio com falha para tentar novamente.");
        }
    }

    private static async Task<IResult> GetAvailabilityAsync(ManageCrmDemoUseCase manage,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(new { available = await manage.GetAvailabilityAsync(cancellationToken) });
        }
        catch (HttpRequestException)
        {
            return Results.Problem(title: "Simulador de CRM indisponível.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = "crm_unavailable" });
        }
    }

    private static async Task<IResult> SetAvailabilityAsync(bool available, HttpContext context,
        IAntiforgery antiforgery, ManageCrmDemoUseCase manage, CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfAsync(context, antiforgery))
            return ApiProblems.BadRequest("invalid_csrf", "Token de proteção inválido.");
        try
        {
            return Results.Ok(new { available = await manage.SetAvailabilityAsync(available, cancellationToken) });
        }
        catch (HttpRequestException)
        {
            return Results.Problem(title: "Simulador de CRM indisponível.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = "crm_unavailable" });
        }
    }

    private static async Task<bool> HasValidCsrfAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
