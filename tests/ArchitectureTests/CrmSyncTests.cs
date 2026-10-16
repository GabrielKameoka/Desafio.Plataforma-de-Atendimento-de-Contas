extern alias crm;
using CrmSimulatorHost = crm::CrmSimulatorHost;
using CrmProjection = crm::CrmProjection;
using CrmEvent = crm::CrmEvent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Endpoints;
using Application.Diagnostics;
using Application.CrmSync;
using Application.Requests;
using Domain;
using Infrastructure.CrmSync;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArchitectureTests;

public sealed class CrmSyncTests
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid Bruno = Guid.Parse("00000000-0000-0000-0000-000000000102");
    private static readonly Guid Clara = Guid.Parse("00000000-0000-0000-0000-000000000103");
    private static readonly Guid Marina = Guid.Parse("00000000-0000-0000-0000-000000000201");

    [PostgresFact]
    public async Task Correlation_reaches_outbox_and_diagnostics_exposes_only_technical_measurements()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var factory = new ApiFactory(database.ConnectionString);
        using var attendant = factory.CreateClient();
        using var supervisor = factory.CreateClient();
        var csrf = await SignInAsync(attendant, Ana);
        await SignInAsync(supervisor, Clara);
        var correlationId = Guid.NewGuid().ToString("N");
        const string privateDescription = "Descrição privada da contestação fictícia";
        using var opening = new HttpRequestMessage(HttpMethod.Post, "/api/requests")
        {
            Content = JsonContent.Create(new { clientId = Marina, description = privateDescription })
        };
        opening.Headers.Add("X-CSRF-TOKEN", csrf);
        opening.Headers.Add("X-Correlation-ID", correlationId);
        opening.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await attendant.SendAsync(opening);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-ID").Single());
        await using (var db = database.CreateContext())
        {
            var message = await db.OutboxMessages.SingleAsync();
            Assert.Equal(correlationId, message.CorrelationId);
            Assert.DoesNotContain(privateDescription, message.Payload);
            Assert.DoesNotContain("Marina Alves", message.Payload);
        }
        using var diagnostics = await supervisor.GetAsync("/api/demo/diagnostics");
        Assert.Equal(HttpStatusCode.OK, diagnostics.StatusCode);
        var snapshot = await diagnostics.Content.ReadFromJsonAsync<DiagnosticsSnapshot>();
        Assert.Equal(1, snapshot?.CaseCount);
        Assert.Equal(1, snapshot?.PendingSyncCount);
        Assert.Contains(snapshot!.Operations, item => item.Operation == "open" && item.Count == 1);
        var serialized = await diagnostics.Content.ReadAsStringAsync();
        Assert.DoesNotContain(privateDescription, serialized);
        Assert.DoesNotContain("Marina Alves", serialized);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await attendant.GetAsync("/api/demo/diagnostics")).StatusCode);
    }

    [PostgresFact]
    public async Task Unexpected_api_error_returns_safe_problem_with_correlation()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var factory = new ApiFactory(database.ConnectionString, failDiagnostics: true);
        using var supervisor = factory.CreateClient();
        await SignInAsync(supervisor, Clara);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/demo/diagnostics");
        request.Headers.Add("X-Correlation-ID", "not-a-guid");
        using var response = await supervisor.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var correlation = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.True(Guid.TryParseExact(correlation, "N", out _));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("internal_error", body);
        Assert.Contains(correlation, body);
        Assert.DoesNotContain("Private customer description", body);
    }

    [PostgresFact]
    public async Task Worker_delivers_in_order_and_crm_deduplicates_without_version_regression()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var crmFactory = new CrmFactory(database.ConnectionString);
        using var crm = crmFactory.CreateClient();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        Guid requestId;
        await using (var openingDb = database.CreateContext())
        {
            var opened = await new OpenFeeDisputeUseCase(new RequestOpeningRepository(openingDb), clock)
                .ExecuteAsync(Marina, Ana, "Contestação fictícia", Guid.NewGuid().ToString());
            requestId = opened.Response.Id;
        }
        await using (var statusDb = database.CreateContext())
        {
            await new HandleRequestUseCase(new RequestHandlingRepository(statusDb), clock)
                .ChangeStatusAsync(requestId, 1, Ana, RequestStatus.UnderReview, "Iniciar análise");
        }
        await using (var assignmentDb = database.CreateContext())
        {
            await new HandleRequestUseCase(new RequestHandlingRepository(assignmentDb), clock)
                .AssignAsync(requestId, 2, Ana, Bruno, "Transferir atendimento");
        }
        await using var workerDb = database.CreateContext();
        var processor = new OutboxProcessor(workerDb, crm, clock);
        Assert.Equal(1, await processor.ProcessBatchAsync());
        Assert.Equal(1, await processor.ProcessBatchAsync());
        Assert.Equal(1, await processor.ProcessBatchAsync());
        Assert.Equal(0, await processor.ProcessBatchAsync());

        await using var verify = database.CreateContext();
        var messages = await verify.OutboxMessages.AsNoTracking()
            .Where(item => item.RequestId == requestId).OrderBy(item => item.RequestVersion).ToListAsync();
        Assert.Equal(3, messages.Count);
        Assert.All(messages, item => Assert.Equal(OutboxStatus.Synced, item.Status));
        var protocol = (await verify.Requests.SingleAsync(item => item.Id == requestId)).Protocol;
        var projection = await crm.GetFromJsonAsync<CrmProjection>($"/cases/{protocol}");
        Assert.NotNull(projection);
        Assert.Equal(3, projection.RequestVersion);
        Assert.Equal("UnderReview", projection.Status);
        Assert.Equal(Bruno, projection.AssigneeId);

        var duplicate = await crm.PostAsync("/events", new StringContent(messages[2].Payload,
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using (var body = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync()))
            Assert.True(body.RootElement.GetProperty("duplicate").GetBoolean());
        var earlier = JsonSerializer.Deserialize<CrmEvent>(messages[0].Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))! with { EventId = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.OK, (await crm.PostAsJsonAsync("/events", earlier)).StatusCode);
        var afterOldEvent = await crm.GetFromJsonAsync<CrmProjection>($"/cases/{protocol}");
        Assert.Equal(3, afterOldEvent?.RequestVersion);
        Assert.Equal(Bruno, afterOldEvent?.AssigneeId);
        Assert.Equal(3, await verify.OutboxMessages.CountAsync(item => item.RequestId == requestId));
    }

    [PostgresFact]
    public async Task Unavailable_crm_exhausts_retries_then_manual_retry_recovers_local_case()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var crmFactory = new CrmFactory(database.ConnectionString);
        using var crm = crmFactory.CreateClient();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.OK, (await crm.PostAsync("/control/unavailable", null)).StatusCode);
        Guid requestId;
        await using (var openingDb = database.CreateContext())
        {
            var opened = await new OpenFeeDisputeUseCase(new RequestOpeningRepository(openingDb), clock)
                .ExecuteAsync(Marina, Ana, "Caso local preservado", Guid.NewGuid().ToString());
            requestId = opened.Response.Id;
        }
        await using (var statusDb = database.CreateContext())
        {
            await new HandleRequestUseCase(new RequestHandlingRepository(statusDb), clock)
                .ChangeStatusAsync(requestId, 1, Ana, RequestStatus.UnderReview, "Analisar localmente");
        }
        await using var workerDb = database.CreateContext();
        var processor = new OutboxProcessor(workerDb, crm, clock);
        Assert.Equal(1, await processor.ProcessBatchAsync());
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await processor.ProcessBatchAsync());
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, await processor.ProcessBatchAsync());
        Assert.Equal(0, await processor.ProcessBatchAsync());

        await using (var verify = database.CreateContext())
        {
            var state = await new GetRequestSyncUseCase(new RequestSyncRepository(verify)).ExecuteAsync(requestId);
            Assert.NotNull(state);
            Assert.Equal("Failed", state.Status);
            Assert.Equal(3, state.Messages[0].AttemptCount);
            Assert.Equal("http_503", state.Messages[0].LastError);
            Assert.Equal("Pending", state.Messages[1].Status);
            var request = await verify.Requests.SingleAsync(item => item.Id == requestId);
            Assert.Equal(2, request.Version);
            Assert.Equal(RequestStatus.UnderReview, request.Status);
        }
        Assert.Equal(HttpStatusCode.OK, (await crm.PostAsync("/control/available", null)).StatusCode);
        await using (var retryDb = database.CreateContext())
        {
            var queued = await new RetryRequestSyncUseCase(new RequestSyncRepository(retryDb), clock)
                .ExecuteAsync(requestId);
            Assert.Equal("Pending", queued.Status);
        }
        Assert.Equal(1, await processor.ProcessBatchAsync());
        Assert.Equal(1, await processor.ProcessBatchAsync());
        await using var finalDb = database.CreateContext();
        var finalState = await new GetRequestSyncUseCase(new RequestSyncRepository(finalDb))
            .ExecuteAsync(requestId);
        Assert.Equal("Synced", finalState?.Status);
        var protocol = (await finalDb.Requests.SingleAsync(item => item.Id == requestId)).Protocol;
        var projection = await crm.GetFromJsonAsync<CrmProjection>($"/cases/{protocol}");
        Assert.Equal(2, projection?.RequestVersion);
        Assert.Equal("UnderReview", projection?.Status);
    }

    [PostgresFact]
    public async Task Api_limits_crm_controls_and_manual_retry_to_supervisor_with_csrf()
    {
        await using var database = await TestDatabase.CreateAsync();
        Guid requestId;
        await using (var db = database.CreateContext())
        {
            requestId = (await new OpenFeeDisputeUseCase(new RequestOpeningRepository(db),
                TimeProvider.System).ExecuteAsync(Marina, Ana, "Caso de integração", Guid.NewGuid().ToString()))
                .Response.Id;
        }
        await using (var db = database.CreateContext())
        {
            var message = await db.OutboxMessages.SingleAsync(item => item.RequestId == requestId);
            message.Status = OutboxStatus.Failed;
            message.AttemptCount = 3;
            message.LastError = "http_503";
            await db.SaveChangesAsync();
        }
        using var factory = new ApiFactory(database.ConnectionString);
        using var anonymous = factory.CreateClient();
        using var attendant = factory.CreateClient();
        using var supervisor = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/demo/crm")).StatusCode);
        var attendantCsrf = await SignInAsync(attendant, Ana);
        var supervisorCsrf = await SignInAsync(supervisor, Clara);
        Assert.Equal(HttpStatusCode.Forbidden, (await attendant.GetAsync("/api/demo/crm")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(attendant, attendantCsrf, $"/api/requests/{requestId}/sync/retry")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await supervisor.PostAsync("/api/demo/crm/unavailable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(supervisor, supervisorCsrf, "/api/demo/crm/unavailable")).StatusCode);
        Assert.False((await supervisor.GetFromJsonAsync<CrmAvailabilityResponse>("/api/demo/crm"))?.Available);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(supervisor, supervisorCsrf, "/api/demo/crm/available")).StatusCode);
        var state = await attendant.GetFromJsonAsync<RequestSyncState>($"/api/requests/{requestId}/sync");
        Assert.Equal("Failed", state?.Status);
        var retried = await SendAsync(supervisor, supervisorCsrf,
            $"/api/requests/{requestId}/sync/retry");
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("Pending", (await retried.Content.ReadFromJsonAsync<RequestSyncState>())?.Status);
    }

    private sealed record CrmAvailabilityResponse(bool Available);

    private static async Task<string> SignInAsync(HttpClient client, Guid userId)
    {
        var token = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session")
        {
            Content = JsonContent.Create(new SelectUserRequest(userId))
        };
        request.Headers.Add("X-CSRF-TOKEN", token!.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        return (await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf"))!.Token;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string csrf, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private sealed class ApiFactory(string connectionString, bool failDiagnostics = false)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ICrmControlGateway, FakeControlGateway>();
                if (failDiagnostics)
                    services.AddScoped<IDiagnosticsRepository, FailingDiagnosticsRepository>();
            });
        }
    }

    private sealed class FailingDiagnosticsRepository : IDiagnosticsRepository
    {
        public Task<(long Cases, long Pending, long Failed)> CountAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Private customer description");
    }

    private sealed class FakeControlGateway : ICrmControlGateway
    {
        private bool available = true;
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(available);
        public Task<bool> SetAvailableAsync(bool next, CancellationToken cancellationToken = default)
        {
            available = next;
            return Task.FromResult(next);
        }
    }

    private sealed class CrmFactory(string connectionString) : WebApplicationFactory<CrmSimulatorHost>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan delta) => current += delta;
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName;
        public string ConnectionString { get; }

        private TestDatabase(string adminConnectionString, string databaseName, string connectionString)
        {
            this.adminConnectionString = adminConnectionString;
            this.databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")!;
            var adminBuilder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
            var name = $"t09_{Guid.NewGuid():N}";
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
                await create.ExecuteNonQueryAsync();
            }
            var database = new TestDatabase(adminBuilder.ConnectionString, name,
                new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString) { Database = name }.ConnectionString);
            await using var db = database.CreateContext();
            await db.Database.MigrateAsync();
            db.Clients.Add(new Client(Marina, "Marina Alves"));
            db.Users.AddRange(new DemoUser(Ana, "Ana Costa", DemoRole.Attendant),
                new DemoUser(Bruno, "Bruno Lima", DemoRole.Attendant),
                new DemoUser(Clara, "Clara Souza", DemoRole.Supervisor));
            await db.SaveChangesAsync();
            return database;
        }

        public DemoDbContext CreateContext() => new(new DbContextOptionsBuilder<DemoDbContext>()
            .UseNpgsql(ConnectionString).Options);

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
