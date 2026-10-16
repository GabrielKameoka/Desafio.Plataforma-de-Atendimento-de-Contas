using Frontend.Components;
using Api.Endpoints;
using Api.Authentication;
using Api.Diagnostics;
using Application.Demo;
using Application.Diagnostics;
using Application.Clients;
using Application.Requests;
using Application.CrmSync;
using Infrastructure.Persistence;
using Infrastructure.CrmSync;
using Infrastructure.Diagnostics;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);
var postgresConnection = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres não foi configurada. Consulte o README para iniciar o PostgreSQL local.");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddAuthentication(DemoAuth.CookieScheme)
    .AddCookie(DemoAuth.CookieScheme, options =>
    {
        options.Cookie.Name = "Atendimento.Demo.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
                await ApiProblems.Unauthorized().ExecuteAsync(context.HttpContext);
            else
                context.Response.Redirect("/");
        };
        options.Events.OnRedirectToAccessDenied = async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
                await ApiProblems.Forbidden().ExecuteAsync(context.HttpContext);
            else
                context.Response.Redirect("/");
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(DemoAuth.StaffPolicy, policy => policy.RequireAuthenticatedUser().RequireRole("Attendant", "Supervisor"))
    .AddPolicy(DemoAuth.AttendantPolicy, policy => policy.RequireAuthenticatedUser().RequireRole("Attendant"))
    .AddPolicy(DemoAuth.SupervisorPolicy, policy => policy.RequireAuthenticatedUser().RequireRole("Supervisor"));
builder.Services.AddDbContext<DemoDbContext>(options =>
    options.UseNpgsql(postgresConnection));
builder.Services.AddScoped<IDemoCatalog, DemoCatalogRepository>();
builder.Services.AddScoped<IDemoUserRepository, DemoUserRepository>();
builder.Services.AddScoped<SelectDemoUserUseCase>();
builder.Services.AddScoped<IRequestOverviewRepository, RequestOverviewRepository>();
builder.Services.AddScoped<GetSupervisorOverviewUseCase>();
builder.Services.AddScoped<IClientContextRepository, ClientContextRepository>();
builder.Services.AddScoped<SearchClientsUseCase>();
builder.Services.AddScoped<GetClientContextUseCase>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DemoDiagnostics>();
builder.Services.AddScoped<IDiagnosticsRepository, DiagnosticsRepository>();
builder.Services.AddScoped<GetDiagnosticsUseCase>();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Atendimento.Demo"))
    .WithMetrics(metrics => metrics
        .AddMeter(DemoDiagnostics.MeterName)
        .AddConsoleExporter((_, reader) =>
            reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 60_000));
builder.Services.AddScoped<IRequestOpeningRepository, RequestOpeningRepository>();
builder.Services.AddScoped<OpenFeeDisputeUseCase>();
builder.Services.AddScoped<IRequestHandlingRepository, RequestHandlingRepository>();
builder.Services.AddScoped<GetRequestDetailUseCase>();
builder.Services.AddScoped<HandleRequestUseCase>();
builder.Services.AddScoped<IRequestQueueRepository, RequestQueueRepository>();
builder.Services.AddScoped<GetRequestQueueUseCase>();
builder.Services.AddScoped<IRequestSyncRepository, RequestSyncRepository>();
builder.Services.AddScoped<GetRequestSyncUseCase>();
builder.Services.AddScoped<RetryRequestSyncUseCase>();
var crmBaseUrl = builder.Configuration["Crm:BaseUrl"]
    ?? "http://crm-simulator:8080";
builder.Services.AddHttpClient<OutboxProcessor>(client =>
{
    client.BaseAddress = new Uri(crmBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient<ICrmControlGateway, CrmControlClient>(client =>
{
    client.BaseAddress = new Uri(crmBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped<ManageCrmDemoUseCase>();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<OutboxWorker>();

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await DemoSeed.ApplyAsync(scope.ServiceProvider.GetRequiredService<DemoDbContext>());
}

app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var correlationId = CorrelationContext.CurrentOrNew();
    app.Logger.LogError("Unhandled operation: {ErrorType} {CorrelationId}",
        error?.GetType().Name ?? "Unknown", correlationId);
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Redirect("/Error");
        return;
    }
    await Results.Problem(title: "Não foi possível concluir a operação.",
        statusCode: StatusCodes.Status500InternalServerError,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "internal_error",
            ["correlationId"] = correlationId
        }).ExecuteAsync(context);
}));

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseStaticFiles();
app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapAuthEndpoints();
app.MapDemoEndpoints();
app.MapClientEndpoints();
app.MapRequestEndpoints();
app.MapRequestHandlingEndpoints();
app.MapRequestQueueEndpoints();
app.MapCrmSyncEndpoints();
app.MapGet("/api/demo/diagnostics", async (GetDiagnosticsUseCase get, CancellationToken ct) =>
    Results.Ok(await get.ExecuteAsync(ct))).RequireAuthorization(DemoAuth.SupervisorPolicy);
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }
