using Application.Diagnostics;

namespace Api.Diagnostics;

public sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers["X-Correlation-ID"].ToString();
        var correlationId = Guid.TryParse(incoming, out var parsed)
            ? parsed.ToString("N") : Guid.NewGuid().ToString("N");
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            return Task.CompletedTask;
        });
        // Blazor server events run over a long-lived circuit. They create an operation ID
        // when saving, instead of reusing the connection request's ID for every action.
        using var correlation = context.Request.Path.StartsWithSegments("/api")
            ? CorrelationContext.Begin(correlationId) : null;
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });
        await next(context);
    }
}
