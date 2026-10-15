using Application.Demo;
using Api.Authentication;

namespace Api.Endpoints;

public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/demo").RequireAuthorization(DemoAuth.StaffPolicy);
        group.MapGet("/catalog", async (IDemoCatalog catalog, CancellationToken cancellationToken) =>
            Results.Ok(await catalog.GetAsync(cancellationToken)));
        group.MapGet("/supervisor/overview", async (GetSupervisorOverviewUseCase overview, CancellationToken cancellationToken) =>
            Results.Ok(await overview.ExecuteAsync(cancellationToken)))
            .RequireAuthorization(DemoAuth.SupervisorPolicy);
        return endpoints;
    }
}
