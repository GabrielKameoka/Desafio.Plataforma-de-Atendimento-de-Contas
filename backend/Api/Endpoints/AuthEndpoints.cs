using System.Text.Json;
using Api.Authentication;
using Application.Demo;
using Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/auth");
        api.MapGet("/users", async (IDemoCatalog catalog, CancellationToken cancellationToken) =>
            Results.Ok(await catalog.GetUsersAsync(cancellationToken)));
        api.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new CsrfTokenResponse(antiforgery.GetAndStoreTokens(context).RequestToken!)));
        api.MapPost("/session", CreateSessionAsync);
        api.MapGet("/session", (HttpContext context) =>
            DemoAuth.CurrentSession(context.User) is { } session
                ? Results.Ok(session)
                : ApiProblems.Unauthorized()).RequireAuthorization(DemoAuth.StaffPolicy);
        api.MapDelete("/session", DeleteSessionAsync).RequireAuthorization(DemoAuth.StaffPolicy);

        endpoints.MapPost("/auth/select", SelectFromFormAsync);
        endpoints.MapPost("/auth/signout", SignOutFromFormAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateSessionAsync(HttpContext context, IAntiforgery antiforgery,
        SelectDemoUserUseCase selectUser, CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfAsync(context, antiforgery))
            return ApiProblems.BadRequest("invalid_csrf", "Token de proteção inválido.");
        if (!context.Request.HasJsonContentType())
            return ApiProblems.BadRequest("invalid_body", "Envie JSON com o ID do usuário fictício.");

        SelectUserRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<SelectUserRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return ApiProblems.BadRequest("invalid_body", "Corpo JSON inválido.");
        }
        if (request is null)
            return ApiProblems.BadRequest("invalid_body", "ID do usuário fictício é obrigatório.");

        var result = await SelectAndSignInAsync(context, selectUser, request.UserId, cancellationToken);
        return result ?? ApiProblems.NotFound("user_not_found", "Usuário fictício não encontrado ou inativo.");
    }

    private static async Task<IResult> DeleteSessionAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await HasValidCsrfAsync(context, antiforgery))
            return ApiProblems.BadRequest("invalid_csrf", "Token de proteção inválido.");
        await context.SignOutAsync(DemoAuth.CookieScheme);
        return Results.Ok(new SessionEndedResponse(true));
    }

    private static async Task<IResult> SelectFromFormAsync(HttpContext context, IAntiforgery antiforgery,
        SelectDemoUserUseCase selectUser, CancellationToken cancellationToken)
    {
        if (!await HasValidCsrfAsync(context, antiforgery))
            return ApiProblems.BadRequest("invalid_csrf", "Token de proteção inválido.");
        if (!context.Request.HasFormContentType)
            return ApiProblems.BadRequest("invalid_body", "Envie o formulário de seleção.");

        var form = await context.Request.ReadFormAsync(cancellationToken);
        if (!Guid.TryParse(form["userId"], out var userId))
            return ApiProblems.BadRequest("invalid_user_id", "Selecione um usuário fictício válido.");
        SelectedDemoUser? selected;
        try
        {
            selected = await selectUser.ExecuteAsync(userId, cancellationToken);
        }
        catch (DomainRuleException error)
        {
            return ApiProblems.BadRequest(error.Code, error.Message);
        }
        if (selected is null)
            return ApiProblems.NotFound("user_not_found", "Usuário fictício não encontrado ou inativo.");

        await DemoAuth.SignInAsync(context, selected);
        return Results.Redirect("/");
    }

    private static async Task<IResult> SignOutFromFormAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await HasValidCsrfAsync(context, antiforgery))
            return ApiProblems.BadRequest("invalid_csrf", "Token de proteção inválido.");
        await context.SignOutAsync(DemoAuth.CookieScheme);
        return Results.Redirect("/");
    }

    private static async Task<IResult?> SelectAndSignInAsync(HttpContext context,
        SelectDemoUserUseCase selectUser, Guid userId, CancellationToken cancellationToken)
    {
        SelectedDemoUser? selected;
        try
        {
            selected = await selectUser.ExecuteAsync(userId, cancellationToken);
        }
        catch (DomainRuleException error)
        {
            return ApiProblems.BadRequest(error.Code, error.Message);
        }
        if (selected is null)
            return null;

        await DemoAuth.SignInAsync(context, selected);
        return Results.Ok(new SessionResponse(selected.Id, selected.DisplayName, selected.Role));
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

public sealed record SelectUserRequest(Guid UserId);
public sealed record CsrfTokenResponse(string Token);
