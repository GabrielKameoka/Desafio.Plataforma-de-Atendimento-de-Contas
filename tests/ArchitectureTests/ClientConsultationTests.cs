using System.Net;
using System.Net.Http.Json;
using Api.Authentication;
using Api.Endpoints;
using Application.Clients;
using Application.Demo;
using Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArchitectureTests;

public sealed class ClientConsultationTests
{
    private static readonly Guid AttendantId = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid MarinaId = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly Guid RafaelId = Guid.Parse("00000000-0000-0000-0000-000000000202");
    private static readonly Guid MarinaRequestId = Guid.Parse("00000000-0000-0000-0000-000000000301");
    private static readonly Guid RafaelRequestId = Guid.Parse("00000000-0000-0000-0000-000000000302");
    private static readonly DateTimeOffset StartedAt = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Context_contains_only_selected_clients_requests_and_attached_contacts_in_order()
    {
        var useCase = new GetClientContextUseCase(new FakeClients());

        var result = await useCase.ExecuteAsync(MarinaId);

        Assert.NotNull(result);
        Assert.Equal(MarinaId, result.Id);
        Assert.Single(result.Requests);
        Assert.Equal(MarinaRequestId, result.Requests[0].Id);
        Assert.Equal(["Primeiro contato", "Segundo contato"],
            result.Interactions.Select(interaction => interaction.Text));
        Assert.All(result.Interactions, interaction => Assert.Equal(MarinaRequestId, interaction.RequestId));
        Assert.DoesNotContain(result.Interactions, interaction => interaction.Text == "Contato de Rafael" ||
            interaction.Text == "Contato sem solicitação");
    }

    [Fact]
    public async Task Search_normalizes_spaces_and_limits_results()
    {
        var repository = new FakeClients();
        var useCase = new SearchClientsUseCase(repository);

        var result = await useCase.ExecuteAsync("  Marina   Alves  ");

        Assert.Single(result);
        Assert.Equal("Marina Alves", repository.LastQuery);
        Assert.Equal(20, repository.LastLimit);
        await Assert.ThrowsAsync<DomainRuleException>(() => useCase.ExecuteAsync("   "));
        await Assert.ThrowsAsync<DomainRuleException>(() => useCase.ExecuteAsync(new string('x', 101)));
    }

    [Fact]
    public async Task Client_api_requires_session_and_uses_problem_details_for_invalid_or_missing_client()
    {
        using var factory = new ClientFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/clients?query=Marina")).StatusCode);
        await SignInAsync(client);

        var invalidQuery = await client.GetAsync("/api/clients?query=%20%20");
        var invalidId = await client.GetAsync("/api/clients/not-a-guid");
        var missing = await client.GetAsync($"/api/clients/{Guid.NewGuid()}");
        var search = await client.GetFromJsonAsync<ClientSearchItem[]>("/api/clients?query=Marina");
        var context = await client.GetFromJsonAsync<ClientContext>($"/api/clients/{MarinaId}");

        Assert.Equal(HttpStatusCode.BadRequest, invalidQuery.StatusCode);
        Assert.Equal(400, (await invalidQuery.Content.ReadFromJsonAsync<ProblemDetails>())?.Status);
        Assert.Equal(HttpStatusCode.BadRequest, invalidId.StatusCode);
        Assert.Equal(400, (await invalidId.Content.ReadFromJsonAsync<ProblemDetails>())?.Status);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(404, (await missing.Content.ReadFromJsonAsync<ProblemDetails>())?.Status);
        Assert.NotNull(search);
        Assert.Single(search);
        Assert.Equal(MarinaId, search[0].Id);
        Assert.Single(context!.Requests);
        Assert.Equal(2, context.Interactions.Count);
    }

    [Fact]
    public async Task Client_pages_render_search_and_context_without_other_clients_data()
    {
        using var factory = new ClientFactory();
        using var client = factory.CreateClient();
        var anonymousDetails = await client.GetStringAsync($"/clients/{MarinaId}");
        Assert.DoesNotContain("Primeiro contato", anonymousDetails);
        Assert.Contains("Selecione um usuário fictício", anonymousDetails);
        await SignInAsync(client);

        var search = await client.GetStringAsync("/clients?query=Marina");
        var details = await client.GetStringAsync($"/clients/{MarinaId}");

        Assert.Contains("Marina Alves", search);
        Assert.Contains($"/clients/{MarinaId}", search);
        Assert.Contains("Histórico de contatos", details);
        Assert.Contains("Primeiro contato", details);
        Assert.Contains("Segundo contato", details);
        Assert.DoesNotContain("Contato de Rafael", details);
        Assert.DoesNotContain("Contato sem solicitação", details);
    }

    private static async Task SignInAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session")
        {
            Content = JsonContent.Create(new SelectUserRequest(AttendantId))
        };
        request.Headers.Add("X-CSRF-TOKEN", Assert.IsType<string>(token?.Token));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class ClientFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", "Host=127.0.0.1;Database=not_used;Username=not_used;Password=not_used");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDemoUserRepository>();
                services.RemoveAll<IClientContextRepository>();
                services.AddSingleton<IDemoUserRepository, FakeUsers>();
                services.AddSingleton<IClientContextRepository, FakeClients>();
            });
        }
    }

    private sealed class FakeUsers : IDemoUserRepository
    {
        public Task<Domain.DemoUser?> FindActiveAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Domain.DemoUser?>(id == AttendantId
                ? new Domain.DemoUser(id, "Ana Costa", DemoRole.Attendant)
                : null);
    }

    private sealed class FakeClients : IClientContextRepository
    {
        public string? LastQuery { get; private set; }
        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<ClientSearchItem>> SearchAsync(string query, int limit,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            LastLimit = limit;
            IReadOnlyList<ClientSearchItem> found = query.Contains("Marina", StringComparison.OrdinalIgnoreCase)
                ? [new(MarinaId, "Marina Alves")]
                : [];
            return Task.FromResult(found);
        }

        public Task<ClientContextData?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (id != MarinaId)
                return Task.FromResult<ClientContextData?>(null);

            ClientRequestData request = new(MarinaRequestId, MarinaId, "DEMO-1", "FeeDispute",
                "Descrição de Marina", "Medium", "Open", AttendantId, "Ana Costa",
                StartedAt, StartedAt.AddDays(3), 1);
            ClientRequestData otherRequest = new(RafaelRequestId, RafaelId, "DEMO-2", "FeeDispute",
                "Descrição de Rafael", "Medium", "Open", AttendantId, "Ana Costa",
                StartedAt, StartedAt.AddDays(3), 1);
            ClientInteractionData first = new(Guid.Parse("00000000-0000-0000-0000-000000000411"),
                MarinaRequestId, AttendantId, "Ana Costa", "Primeiro contato", StartedAt.AddHours(1));
            ClientInteractionData second = new(Guid.Parse("00000000-0000-0000-0000-000000000412"),
                MarinaRequestId, AttendantId, "Ana Costa", "Segundo contato", StartedAt.AddHours(2));
            ClientInteractionData other = new(Guid.NewGuid(), RafaelRequestId, AttendantId,
                "Ana Costa", "Contato de Rafael", StartedAt.AddHours(3));
            ClientInteractionData orphan = new(Guid.NewGuid(), Guid.NewGuid(), AttendantId,
                "Ana Costa", "Contato sem solicitação", StartedAt.AddHours(4));

            return Task.FromResult<ClientContextData?>(new ClientContextData(
                new ClientSearchItem(MarinaId, "Marina Alves"),
                [otherRequest, request], [orphan, second, other, first]));
        }
    }
}
