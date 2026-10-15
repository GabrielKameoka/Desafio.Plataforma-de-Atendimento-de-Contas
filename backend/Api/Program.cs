using Frontend.Components;
using Api.Endpoints;
using Api.Authentication;
using Application.Demo;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

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
    .AddPolicy(DemoAuth.SupervisorPolicy, policy => policy.RequireAuthenticatedUser().RequireRole("Supervisor"));
builder.Services.AddDbContext<DemoDbContext>(options =>
    options.UseNpgsql(postgresConnection));
builder.Services.AddScoped<IDemoCatalog, DemoCatalogRepository>();
builder.Services.AddScoped<IDemoUserRepository, DemoUserRepository>();
builder.Services.AddScoped<SelectDemoUserUseCase>();
builder.Services.AddScoped<IRequestOverviewRepository, RequestOverviewRepository>();
builder.Services.AddScoped<GetSupervisorOverviewUseCase>();

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await DemoSeed.ApplyAsync(scope.ServiceProvider.GetRequiredService<DemoDbContext>());
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapAuthEndpoints();
app.MapDemoEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }
