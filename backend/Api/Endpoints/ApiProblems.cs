namespace Api.Endpoints;

public static class ApiProblems
{
    public static IResult BadRequest(string code, string title) =>
        Results.Problem(title: title, statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult NotFound(string code, string title) =>
        Results.Problem(title: title, statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult Conflict(string code, string title) =>
        Results.Problem(title: title, statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult Unauthorized() =>
        Results.Problem(title: "Sessão necessária.", statusCode: StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["code"] = "session_required" });

    public static IResult Forbidden() =>
        Results.Problem(title: "Seu papel não permite esta operação.", statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["code"] = "role_forbidden" });
}
