using Api.Components;
using Application.Demo;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddDbContext<DemoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<IDemoCatalog, DemoCatalogRepository>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
    await DemoSeed.ApplyAsync(scope.ServiceProvider.GetRequiredService<DemoDbContext>());

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapGet("/api/demo/catalog", async (IDemoCatalog catalog, CancellationToken cancellationToken) =>
    Results.Ok(await catalog.GetAsync(cancellationToken)));
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Frontend.DemoHome).Assembly);

app.Run();
