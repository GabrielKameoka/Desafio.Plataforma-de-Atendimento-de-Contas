using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class RequestHandlingTests
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid Bruno = Guid.Parse("00000000-0000-0000-0000-000000000102");
    private static readonly Guid Clara = Guid.Parse("00000000-0000-0000-0000-000000000103");
    private static readonly Guid Marina = Guid.Parse("00000000-0000-0000-0000-000000000201");

    [PostgresFact]
    public async Task Attendant_can_conduct_case_and_history_records_each_decision()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = await database.OpenAsync();
        using var factory = new TestFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var csrf = await SignInAsync(client, Bruno);

        var assignment = await SendAsync(client, csrf, id, "assignment",
            new ChangeRequestBody(1, AssigneeId: Bruno, Reason: "Assumir atendimento"));
        Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
        var interaction = await SendAsync(client, csrf, id, "interactions",
            new ChangeRequestBody(2, Text: "Cliente confirmou a tarifa"));
        Assert.Equal(HttpStatusCode.OK, interaction.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, csrf, id, "status",
            new ChangeRequestBody(3, Status: "UnderReview", Reason: "Analisar cobrança"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, csrf, id, "priority",
            new ChangeRequestBody(4, Priority: "High", Reason: "Urgência confirmada"))).StatusCode);
        var deadline = DateTimeOffset.UtcNow.AddDays(8);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, csrf, id, "deadline",
            new ChangeRequestBody(5, DeadlineAtUtc: deadline, Reason: "Prazo combinado"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, csrf, id, "status",
            new ChangeRequestBody(6, Status: "Resolved", Reason: "Tarifa estornada"))).StatusCode);

        var detail = await client.GetFromJsonAsync<RequestDetail>($"/api/requests/{id}");
        Assert.NotNull(detail);
        Assert.Equal(7, detail.Version);
        Assert.Equal("Resolved", detail.Status);
        Assert.Equal(Bruno, detail.AssigneeId);
        Assert.Equal("High", detail.Priority);
        Assert.Single(detail.Interactions);
        Assert.Equal("Cliente confirmou a tarifa", detail.Interactions[0].Text);
        Assert.Equal(6, detail.AuditEvents.Count);
        Assert.Contains(detail.AuditEvents, item => item.Type == "AssigneeChanged" &&
            item.PreviousValue == Ana.ToString() && item.NewValue == Bruno.ToString() &&
            item.AuthorId == Bruno && item.Reason == "Assumir atendimento");
        Assert.Contains(detail.AuditEvents, item => item.Type == "StatusChanged" &&
            item.PreviousValue == "UnderReview" && item.NewValue == "Resolved" &&
            item.Reason == "Tarifa estornada");
        Assert.Equal("Pending", detail.SyncStatus);

        await using var db = database.CreateContext();
        Assert.Equal(4, await db.OutboxMessages.CountAsync());
        Assert.All(await db.OutboxMessages.Select(item => item.Payload).ToListAsync(), payload =>
        {
            Assert.DoesNotContain("Cliente confirmou", payload);
            Assert.DoesNotContain("Tarifa estornada", payload);
        });
    }

    [PostgresFact]
    public async Task Stale_version_and_invalid_transition_do_not_append_history()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = await database.OpenAsync();
        using var factory = new TestFactory(database.ConnectionString);
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        var firstCsrf = await SignInAsync(first, Ana);
        var secondCsrf = await SignInAsync(second, Bruno);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(first, firstCsrf, id, "status",
            new ChangeRequestBody(1, Status: "UnderReview", Reason: "Iniciar análise"))).StatusCode);
        var stale = await SendAsync(second, secondCsrf, id, "assignment",
            new ChangeRequestBody(1, AssigneeId: Bruno, Reason: "Assumir"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()))
        {
            Assert.Equal("version_conflict", problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(2, problem.RootElement.GetProperty("currentVersion").GetInt64());
            Assert.Equal(id.ToString(), problem.RootElement.GetProperty("requestId").GetString());
            Assert.DoesNotContain("Assumir", problem.RootElement.GetRawText());
        }
        var invalid = await SendAsync(first, firstCsrf, id, "status",
            new ChangeRequestBody(2, Status: "Open", Reason: "Voltar"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var detail = await first.GetFromJsonAsync<RequestDetail>($"/api/requests/{id}");
        Assert.NotNull(detail);
        Assert.Equal(2, detail.Version);
        Assert.Equal(2, detail.AuditEvents.Count);
        Assert.Equal(2, await database.CountOutboxAsync());
    }

    [PostgresFact]
    public async Task Concurrent_writers_update_only_one_version_and_leave_one_complete_event()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = await database.OpenAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;

        async Task<RequestSaveResult> WriteAsync(RequestPriority priority)
        {
            await using var db = database.CreateContext();
            var repository = new RequestHandlingRepository(db);
            var request = await repository.FindRequestAsync(id) ?? throw new Exception("Caso não encontrado");
            var actor = await repository.FindAttendantAsync(Ana) ?? throw new Exception("Atendente não encontrado");
            request.ChangePriority(priority, actor, "Decisão concorrente", TimeProvider.System);
            var audit = Assert.Single(request.AuditEvents);
            if (Interlocked.Increment(ref arrivals) == 2)
                gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return await repository.SaveAsync(request, 1, audit, null, null);
        }

        var results = await Task.WhenAll(WriteAsync(RequestPriority.High),
            WriteAsync(RequestPriority.Low)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(results, item => item.Saved);
        Assert.Single(results, item => !item.Saved && item.CurrentVersion == 2);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.Requests.Where(item => item.Id == id).Select(item => item.Version).SingleAsync());
        Assert.Equal(2, await verify.AuditEvents.CountAsync(item => item.RequestId == id));
        Assert.Equal(1, await verify.OutboxMessages.CountAsync(item => item.RequestId == id));
    }

    [PostgresFact]
    public async Task Failure_after_conditional_update_rolls_back_status_audit_and_outbox()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = await database.OpenAsync();
        var options = new DbContextOptionsBuilder<DemoDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(new ThrowAfterSaveInterceptor()).Options;
        await using (var db = new DemoDbContext(options))
        {
            var useCase = new HandleRequestUseCase(new RequestHandlingRepository(db), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ChangeStatusAsync(
                id, 1, Ana, RequestStatus.UnderReview, "Analisar cobrança"));
        }
        await using var verify = database.CreateContext();
        var request = await verify.Requests.SingleAsync(item => item.Id == id);
        Assert.Equal(1, request.Version);
        Assert.Equal(RequestStatus.Open, request.Status);
        Assert.Equal(1, await verify.AuditEvents.CountAsync(item => item.RequestId == id));
        Assert.Equal(1, await verify.OutboxMessages.CountAsync(item => item.RequestId == id));
    }

    [PostgresFact]
    public async Task Resolved_case_and_supervisor_cannot_make_attendant_changes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = await database.OpenAsync();
        using var factory = new TestFactory(database.ConnectionString);
        using var attendant = factory.CreateClient();
        using var supervisor = factory.CreateClient();
        var csrf = await SignInAsync(attendant, Ana);
        var supervisorCsrf = await SignInAsync(supervisor, Clara);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(supervisor, supervisorCsrf, id, "status",
            new ChangeRequestBody(1, Status: "UnderReview", Reason: "Tentativa"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(attendant, csrf, id, "status",
            new ChangeRequestBody(1, Status: "UnderReview", Reason: "Análise"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(attendant, csrf, id, "status",
            new ChangeRequestBody(2, Status: "Resolved", Reason: "Concluído"))).StatusCode);
        var rejected = await SendAsync(attendant, csrf, id, "interactions",
            new ChangeRequestBody(3, Text: "Interação tardia"));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var detail = await attendant.GetFromJsonAsync<RequestDetail>($"/api/requests/{id}");
        Assert.NotNull(detail);
        Assert.Equal(3, detail.Version);
        Assert.Empty(detail.Interactions);
        Assert.Equal(3, detail.AuditEvents.Count);
    }

    [PostgresFact]
    public async Task Supervisor_filters_due_cases_including_waiting_for_customer()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var nearId = await CreateCaseAsync(database, now.AddDays(-1), now.AddHours(8),
            RequestStatus.UnderReview, RequestPriority.High, Ana);
        var overdueId = await CreateCaseAsync(database, now.AddDays(-10), now.AddHours(-8),
            RequestStatus.WaitingForCustomer, RequestPriority.Medium, Bruno);
        await CreateCaseAsync(database, now.AddDays(-10), now.AddHours(-8),
            RequestStatus.Resolved, RequestPriority.Medium, Ana);
        using var factory = new TestFactory(database.ConnectionString);
        using var anonymous = factory.CreateClient();
        using var attendant = factory.CreateClient();
        using var supervisor = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/requests?due=Overdue")).StatusCode);
        await SignInAsync(attendant, Ana);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await attendant.GetAsync("/api/requests?due=Overdue")).StatusCode);
        await SignInAsync(supervisor, Clara);

        var overdue = await supervisor.GetFromJsonAsync<RequestQueuePage>(
            "/api/requests?due=Overdue&status=WaitingForCustomer&assigneeId=" + Bruno);
        Assert.NotNull(overdue);
        Assert.Equal(1, overdue.Total);
        Assert.Equal(overdueId, Assert.Single(overdue.Items).Id);
        Assert.Equal("Overdue", overdue.Items[0].DueState);
        var near = await supervisor.GetFromJsonAsync<RequestQueuePage>(
            "/api/requests?due=DueSoon&priority=High&assigneeId=" + Ana);
        Assert.NotNull(near);
        Assert.Equal(nearId, Assert.Single(near.Items).Id);
        Assert.Equal("DueSoon", near.Items[0].DueState);
        var firstPage = await supervisor.GetFromJsonAsync<RequestQueuePage>("/api/requests?page=1&pageSize=1");
        Assert.NotNull(firstPage);
        Assert.Equal(3, firstPage.Total);
        Assert.Single(firstPage.Items);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await supervisor.GetAsync("/api/requests?due=tomorrow")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await supervisor.GetAsync("/api/requests?pageSize=500")).StatusCode);
    }

    [PostgresFact]
    public async Task Only_supervisor_reopens_resolved_case_with_reason_and_version()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = await database.OpenAsync();
        using var factory = new TestFactory(database.ConnectionString);
        using var anonymous = factory.CreateClient();
        using var attendant = factory.CreateClient();
        using var supervisor = factory.CreateClient();
        var attendantCsrf = await SignInAsync(attendant, Ana);
        var supervisorCsrf = await SignInAsync(supervisor, Clara);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(supervisor, supervisorCsrf, id, "reopen",
                new ChangeRequestBody(1, Reason: "Ainda aberto"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(attendant, attendantCsrf, id, "status",
            new ChangeRequestBody(1, Status: "UnderReview", Reason: "Análise"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(attendant, attendantCsrf, id, "status",
            new ChangeRequestBody(2, Status: "Resolved", Reason: "Resolvido"))).StatusCode);

        var body = new ChangeRequestBody(3, Reason: "Revisão da solução");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync($"/api/requests/{id}/reopen", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(attendant, attendantCsrf, id, "reopen", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await supervisor.PostAsJsonAsync($"/api/requests/{id}/reopen", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(supervisor, supervisorCsrf, id, "reopen",
                new ChangeRequestBody(3, Reason: " "))).StatusCode);
        var reopened = await SendAsync(supervisor, supervisorCsrf, id, "reopen", body);
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var detail = await reopened.Content.ReadFromJsonAsync<RequestDetail>();
        Assert.NotNull(detail);
        Assert.Equal("UnderReview", detail.Status);
        Assert.Equal(4, detail.Version);
        Assert.Contains(detail.AuditEvents, item => item.Type == "Reopened" &&
            item.PreviousValue == "Resolved" && item.NewValue == "UnderReview" &&
            item.AuthorId == Clara && item.Reason == "Revisão da solução");
        Assert.Equal(HttpStatusCode.Conflict,
            (await SendAsync(supervisor, supervisorCsrf, id, "reopen", body)).StatusCode);
        await using var verify = database.CreateContext();
        Assert.Equal(4, await verify.AuditEvents.CountAsync(item => item.RequestId == id));
        Assert.Equal(4, await verify.OutboxMessages.CountAsync(item => item.RequestId == id));
    }

    private static async Task<Guid> CreateCaseAsync(TestDatabase database, DateTimeOffset opened,
        DateTimeOffset deadline, RequestStatus status, RequestPriority priority, Guid assigneeId)
    {
        await using var db = database.CreateContext();
        var client = await db.Clients.SingleAsync(item => item.Id == Marina);
        var attendant = await db.Users.SingleAsync(item => item.Id == assigneeId);
        var id = Guid.NewGuid();
        var request = ServiceRequest.OpenFeeDispute(id, $"TEST-{id:N}", client, attendant,
            "Caso de supervisão", Guid.NewGuid().ToString(), new FixedTimeProvider(opened));
        if (priority != RequestPriority.Medium)
            request.ChangePriority(priority, attendant, "Prioridade do teste", new FixedTimeProvider(opened.AddMinutes(10)));
        request.ChangeDeadline(deadline, attendant, "Prazo do teste", new FixedTimeProvider(opened.AddMinutes(20)));
        request.ChangeStatus(RequestStatus.UnderReview, attendant, "Análise do teste",
            new FixedTimeProvider(opened.AddMinutes(30)));
        if (status != RequestStatus.UnderReview)
            request.ChangeStatus(status, attendant, "Andamento do teste",
                new FixedTimeProvider(opened.AddMinutes(40)));
        db.Requests.Add(request);
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string csrf, Guid id,
        string operation, ChangeRequestBody body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/requests/{id}/{operation}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<string> SignInAsync(HttpClient client, Guid userId)
    {
        var initial = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session")
        {
            Content = JsonContent.Create(new SelectUserRequest(userId))
        };
        request.Headers.Add("X-CSRF-TOKEN", initial!.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        return (await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf"))!.Token;
    }

    private sealed class TestFactory(string connectionString) : WebApplicationFactory<Program>
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
            throw new InvalidOperationException("Falha após inserir histórico, antes do commit.");
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
            var name = $"t07_{Guid.NewGuid():N}";
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

        public async Task<Guid> OpenAsync()
        {
            await using var db = CreateContext();
            return (await new OpenFeeDisputeUseCase(new RequestOpeningRepository(db), TimeProvider.System)
                .ExecuteAsync(Marina, Ana, "Contestação de tarifa", Guid.NewGuid().ToString())).Response.Id;
        }

        public async Task<int> CountOutboxAsync()
        {
            await using var db = CreateContext();
            return await db.OutboxMessages.CountAsync();
        }

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
