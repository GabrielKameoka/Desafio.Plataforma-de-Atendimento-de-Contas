using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Api.Authentication;
using Api.Endpoints;
using Application.Demo;
using Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArchitectureTests;

public sealed class AuthenticationEndpointTests
{
    private static readonly Guid AttendantId = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid SupervisorId = Guid.Parse("00000000-0000-0000-0000-000000000103");

    [Fact]
    public async Task Protected_catalog_returns_401_problem_without_session()
    {
        using var factory = new DemoFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/demo/catalog");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(401, problem?.Status);
    }

    [Fact]
    public async Task Attendant_can_read_catalog_but_receives_403_for_supervisor_overview()
    {
        using var factory = new DemoFactory();
        using var client = factory.CreateClient();
        await SignInAsync(client, AttendantId);

        var catalog = await client.GetAsync("/api/demo/catalog");
        var overview = await client.GetAsync("/api/demo/supervisor/overview");

        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, overview.StatusCode);
        var problem = await overview.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(403, problem?.Status);
    }

    [Fact]
    public async Task Supervisor_can_read_overview_and_role_comes_from_server()
    {
        using var factory = new DemoFactory();
        using var client = factory.CreateClient();
        await SignInAsync(client, SupervisorId);

        var session = await client.GetFromJsonAsync<SessionResponse>("/api/auth/session");
        var overview = await client.GetFromJsonAsync<SupervisorOverview>("/api/demo/supervisor/overview");

        Assert.Equal("Supervisor", session?.Role);
        Assert.Equal(7, overview?.TotalRequests);
    }

    [Fact]
    public async Task Missing_csrf_and_empty_user_id_return_400_problem_without_creating_session()
    {
        using var factory = new DemoFactory();
        using var client = factory.CreateClient();

        var withoutCsrf = await client.PostAsJsonAsync("/api/auth/session", new SelectUserRequest(AttendantId));
        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);
        Assert.Equal(400, (await withoutCsrf.Content.ReadFromJsonAsync<ProblemDetails>())?.Status);

        var token = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session")
        {
            Content = JsonContent.Create(new SelectUserRequest(Guid.Empty))
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        var invalid = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(400, (await invalid.Content.ReadFromJsonAsync<ProblemDetails>())?.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Home_form_selects_user_and_then_shows_authenticated_catalog()
    {
        using var factory = new DemoFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var anonymousHome = await client.GetStringAsync("/");
        Assert.Contains("Escolha um usuário fictício", anonymousHome);
        Assert.DoesNotContain("Clientes fictícios", anonymousHome);

        var input = Regex.Match(anonymousHome, "<input[^>]*__RequestVerificationToken[^>]*>");
        Assert.True(input.Success, "O formulário precisa emitir um token antiforgery.");
        var token = Regex.Match(input.Value, "value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        var response = await client.PostAsync("/auth/select", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("userId", AttendantId.ToString()),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var signedInHome = await client.GetStringAsync("/");
        Assert.Contains("Ana Costa", signedInHome);
        Assert.Contains("Clientes fictícios", signedInHome);

        var signOutInput = Regex.Match(signedInHome, "<input[^>]*__RequestVerificationToken[^>]*>");
        var signOutToken = Regex.Match(signOutInput.Value, "value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(signOutToken);
        var signOut = await client.PostAsync("/auth/signout", new FormUrlEncodedContent(
        [new KeyValuePair<string, string>("__RequestVerificationToken", signOutToken)]));
        Assert.Equal(HttpStatusCode.Redirect, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/demo/catalog")).StatusCode);
    }

    private static async Task SignInAsync(HttpClient client, Guid id)
    {
        var token = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session")
        {
            Content = JsonContent.Create(new SelectUserRequest(id))
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        return Assert.IsType<string>(token?.Token);
    }

    private sealed class DemoFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", "Host=127.0.0.1;Database=not_used;Username=not_used;Password=not_used");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDemoCatalog>();
                services.RemoveAll<IDemoUserRepository>();
                services.RemoveAll<IRequestOverviewRepository>();
                services.AddSingleton<IDemoCatalog, FakeCatalog>();
                services.AddSingleton<IDemoUserRepository, FakeUsers>();
                services.AddSingleton<IRequestOverviewRepository, FakeOverview>();
            });
        }
    }

    private sealed class FakeCatalog : IDemoCatalog
    {
        private static readonly IReadOnlyList<Application.Demo.DemoUser> Users =
        [
            new(AttendantId, "Ana Costa", "Attendant"),
            new(SupervisorId, "Clara Souza", "Supervisor")
        ];

        public Task<DemoCatalog> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DemoCatalog(Users, [new Application.Demo.DemoClient(Guid.NewGuid(), "Cliente fictício")]));

        public Task<IReadOnlyList<Application.Demo.DemoUser>> GetUsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Users);
    }

    private sealed class FakeUsers : IDemoUserRepository
    {
        public Task<Domain.DemoUser?> FindActiveAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Domain.DemoUser? user = id == AttendantId
                ? new(id, "Ana Costa", DemoRole.Attendant)
                : id == SupervisorId
                    ? new(id, "Clara Souza", DemoRole.Supervisor)
                    : null;
            return Task.FromResult(user);
        }
    }

    private sealed class FakeOverview : IRequestOverviewRepository
    {
        public Task<int> CountRequestsAsync(CancellationToken cancellationToken = default) => Task.FromResult(7);
    }
}
