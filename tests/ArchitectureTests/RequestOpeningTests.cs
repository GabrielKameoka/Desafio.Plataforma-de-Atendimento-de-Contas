using System.Net;
using System.Net.Http.Json;
using Api.Endpoints;
using Application.Requests;
using Domain;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ArchitectureTests;

public sealed class RequestOpeningTests
{
    private static readonly Guid AttendantId = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid SupervisorId = Guid.Parse("00000000-0000-0000-0000-000000000103");
    private static readonly Guid ClientId = Guid.Parse("00000000-0000-0000-0000-000000000201");

    [PostgresFact]
    public async Task Http_opening_replays_original_response_and_rejects_changed_content()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var factory = new OpeningFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/requests",
            new OpenRequestBody(ClientId, "Contestação de tarifa"))).StatusCode);
        var csrf = await SignInAsync(client);
        var key = Guid.NewGuid().ToString();

        var created = await SendOpenAsync(client, csrf, key, ClientId, " Contestação de tarifa ");
        var replay = await SendOpenAsync(client, csrf, key, ClientId, "Contestação de tarifa");
        var conflict = await SendOpenAsync(client, csrf, key, ClientId, "Outro conteúdo");
        using var supervisor = factory.CreateClient();
        var supervisorCsrf = await SignInAsync(supervisor, SupervisorId);
        var forbidden = await SendOpenAsync(supervisor, supervisorCsrf,
            Guid.NewGuid().ToString(), ClientId, "Contestação de supervisor");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<OpenRequestResponse>();
        var again = await replay.Content.ReadFromJsonAsync<OpenRequestResponse>();
        Assert.NotNull(first);
        Assert.Equal(first, again);
        Assert.StartsWith("ATD-", first.Protocol);
        Assert.Equal("FeeDispute", first.Type);
        Assert.Equal("Medium", first.Priority);
        Assert.Equal("Open", first.Status);
        Assert.Equal(AttendantId, first.AssigneeId);
        Assert.Equal(BusinessDeadline.Calculate(first.OpenedAtUtc, RequestPriority.Medium), first.DeadlineAtUtc);
        Assert.Contains("idempotency_conflict", await conflict.Content.ReadAsStringAsync());

        await using var db = database.CreateContext();
        Assert.Equal(1, await db.Requests.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync());
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(1, await db.OutboxMessages.CountAsync());
    }

    [PostgresFact]
    public async Task Invalid_input_and_missing_client_leave_no_partial_records()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var factory = new OpeningFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var csrf = await SignInAsync(client);

        var invalid = await SendOpenAsync(client, csrf, Guid.NewGuid().ToString(), ClientId, "  ");
        var missing = await SendOpenAsync(client, csrf, Guid.NewGuid().ToString(), Guid.NewGuid(), "Contestação");
        var missingKey = await SendOpenAsync(client, csrf, null, ClientId, "Contestação");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(0, await db.Requests.CountAsync());
        Assert.Equal(0, await db.AuditEvents.CountAsync());
        Assert.Equal(0, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(0, await db.OutboxMessages.CountAsync());
    }

    [PostgresFact]
    public async Task Failure_after_database_writes_rolls_back_case_key_audit_and_outbox()
    {
        await using var database = await TestDatabase.CreateAsync();
        var options = new DbContextOptionsBuilder<DemoDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(new ThrowAfterSaveInterceptor())
            .Options;
        await using (var failingDb = new DemoDbContext(options))
        {
            var useCase = new OpenFeeDisputeUseCase(new RequestOpeningRepository(failingDb), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(
                ClientId, AttendantId, "Falha controlada", Guid.NewGuid().ToString()));
        }

        await using var db = database.CreateContext();
        Assert.Equal(0, await db.Requests.CountAsync());
        Assert.Equal(0, await db.AuditEvents.CountAsync());
        Assert.Equal(0, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(0, await db.OutboxMessages.CountAsync());
    }

    [PostgresFact]
    public async Task Simultaneous_openings_with_same_key_create_one_case()
    {
        await using var database = await TestDatabase.CreateAsync();
        var gate = new TwoReaderGate();
        var key = Guid.NewGuid().ToString();

        async Task<OpenRequestResult> OpenAsync()
        {
            await using var db = database.CreateContext();
            var repository = new CoordinatedRepository(new RequestOpeningRepository(db), gate);
            return await new OpenFeeDisputeUseCase(repository, TimeProvider.System)
                .ExecuteAsync(ClientId, AttendantId, "Mesmo conteúdo", key);
        }

        var results = await Task.WhenAll(OpenAsync(), OpenAsync()).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Single(results, result => result.WasCreated);
        Assert.Equal(results[0].Response, results[1].Response);
        await using var verify = database.CreateContext();
        Assert.Equal(1, await verify.Requests.CountAsync());
        Assert.Equal(1, await verify.AuditEvents.CountAsync());
        Assert.Equal(1, await verify.IdempotencyRecords.CountAsync());
        Assert.Equal(1, await verify.OutboxMessages.CountAsync());
    }

    private static async Task<HttpResponseMessage> SendOpenAsync(HttpClient client, string csrf,
        string? key, Guid clientId, string description)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/requests")
        {
            Content = JsonContent.Create(new OpenRequestBody(clientId, description))
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<string> SignInAsync(HttpClient client, Guid? userId = null)
    {
        var token = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        var csrf = Assert.IsType<string>(token?.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session")
        {
            Content = JsonContent.Create(new SelectUserRequest(userId ?? AttendantId))
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        var signedInToken = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        return Assert.IsType<string>(signedInToken?.Token);
    }

    private sealed class OpeningFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        }
    }

    private sealed class ThrowAfterSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Falha de teste depois das gravações, antes do commit.");
    }

    private sealed class TwoReaderGate
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public async Task WaitAsync()
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                completion.SetResult();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class CoordinatedRepository(IRequestOpeningRepository inner, TwoReaderGate gate)
        : IRequestOpeningRepository
    {
        public async Task<StoredOpening?> FindByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            var found = await inner.FindByKeyAsync(key, cancellationToken);
            if (found is null)
                await gate.WaitAsync();
            return found;
        }

        public Task<Client?> FindClientAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.FindClientAsync(id, cancellationToken);

        public Task<DemoUser?> FindAttendantAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.FindAttendantAsync(id, cancellationToken);

        public Task<StoredOpening> CreateOrGetAsync(ServiceRequest request, string key,
            string requestHash, OpenRequestResponse response, CancellationToken cancellationToken = default) =>
            inner.CreateOrGetAsync(request, key, requestHash, response, cancellationToken);
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
            var configured = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("Defina TEST_POSTGRES_CONNECTION para executar a integração PostgreSQL.");

            var adminBuilder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
            var databaseName = $"t06_{Guid.NewGuid():N}";
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin);
                await create.ExecuteNonQueryAsync();
            }

            var testBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
            {
                Database = databaseName
            };
            var database = new TestDatabase(adminBuilder.ConnectionString, databaseName,
                testBuilder.ConnectionString);
            await using var db = database.CreateContext();
            await db.Database.MigrateAsync();
            db.Clients.Add(new Client(ClientId, "Marina Alves"));
            db.Users.Add(new DemoUser(AttendantId, "Ana Costa", DemoRole.Attendant));
            db.Users.Add(new DemoUser(SupervisorId, "Clara Souza", DemoRole.Supervisor));
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

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")))
            Skip = "Defina TEST_POSTGRES_CONNECTION para executar a integração PostgreSQL.";
    }
}
