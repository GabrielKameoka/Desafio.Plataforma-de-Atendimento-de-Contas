using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres não foi configurada.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<CrmStore>();
builder.Services.AddSingleton<CrmAvailability>();

var app = builder.Build();
await app.Services.GetRequiredService<CrmStore>().InitializeAsync();

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapGet("/control/state", (CrmAvailability availability) =>
    Results.Ok(new { available = availability.IsAvailable }));
app.MapPost("/control/unavailable", (CrmAvailability availability) =>
{
    availability.IsAvailable = false;
    return Results.Ok(new { available = false });
});
app.MapPost("/control/available", (CrmAvailability availability) =>
{
    availability.IsAvailable = true;
    return Results.Ok(new { available = true });
});
app.MapPost("/events", async (CrmEvent? message, CrmAvailability availability,
    CrmStore store, CancellationToken cancellationToken) =>
{
    if (!availability.IsAvailable)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    if (message is null || !message.IsValid())
        return Results.BadRequest(new { code = "invalid_event" });
    var duplicate = await store.AcceptAsync(message, cancellationToken);
    return Results.Ok(new { accepted = true, duplicate });
});
app.MapGet("/cases/{protocol}", async (string protocol, CrmStore store,
    CancellationToken cancellationToken) =>
    await store.FindAsync(protocol, cancellationToken) is { } projection
        ? Results.Ok(projection) : Results.NotFound());
app.MapGet("/events/{eventId:guid}", async (Guid eventId, CrmStore store,
    CancellationToken cancellationToken) =>
    Results.Ok(new { accepted = await store.HasEventAsync(eventId, cancellationToken) }));

app.Run();

public sealed class CrmSimulatorHost;
