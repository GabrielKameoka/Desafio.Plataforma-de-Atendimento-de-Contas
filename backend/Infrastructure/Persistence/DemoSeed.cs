using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public static class DemoSeed
{
    public static async Task ApplyAsync(DemoDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        // Stable identifiers make restarts safe and keep future demo scenarios reproducible.
        var users = new[]
        {
            new DemoUser(Guid.Parse("00000000-0000-0000-0000-000000000101"), "Ana Costa", DemoRole.Attendant),
            new DemoUser(Guid.Parse("00000000-0000-0000-0000-000000000102"), "Bruno Lima", DemoRole.Attendant),
            new DemoUser(Guid.Parse("00000000-0000-0000-0000-000000000103"), "Clara Souza", DemoRole.Supervisor)
        };
        var clients = new[]
        {
            new Client(Guid.Parse("00000000-0000-0000-0000-000000000201"), "Marina Alves"),
            new Client(Guid.Parse("00000000-0000-0000-0000-000000000202"), "Rafael Nunes")
        };

        foreach (var user in users)
        {
            if (!await dbContext.Users.AnyAsync(existing => existing.Id == user.Id, cancellationToken))
                dbContext.Users.Add(user);
        }
        foreach (var client in clients)
        {
            if (!await dbContext.Clients.AnyAsync(existing => existing.Id == client.Id, cancellationToken))
                dbContext.Clients.Add(client);
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        await AddPreviousCaseAsync(dbContext,
            Guid.Parse("00000000-0000-0000-0000-000000000301"), "DEMO-2026-0001",
            clients[0].Id, users[0].Id, "Contestação anterior de tarifa de manutenção.",
            new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero),
            [
                (Guid.Parse("00000000-0000-0000-0000-000000000401"), users[0].Id,
                    "Cliente informou a cobrança para análise.", new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero)),
                (Guid.Parse("00000000-0000-0000-0000-000000000402"), users[1].Id,
                    "Atendimento retornou com orientação sobre a tarifa.", new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero))
            ], cancellationToken);

        await AddPreviousCaseAsync(dbContext,
            Guid.Parse("00000000-0000-0000-0000-000000000302"), "DEMO-2026-0002",
            clients[1].Id, users[1].Id, "Dúvida anterior sobre tarifa de serviço.",
            new DateTimeOffset(2026, 10, 3, 13, 0, 0, TimeSpan.Zero),
            [
                (Guid.Parse("00000000-0000-0000-0000-000000000403"), users[1].Id,
                    "Cliente solicitou esclarecimento da tarifa.", new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.Zero))
            ], cancellationToken);

        var nowUtc = DateTimeOffset.UtcNow;
        await AddSupervisionCaseAsync(dbContext,
            Guid.Parse("00000000-0000-0000-0000-000000000303"), "DEMO-2026-0003",
            clients[0].Id, users[0].Id, "Contestação em acompanhamento de prazo.",
            nowUtc.AddDays(-1), nowUtc.AddHours(12), RequestStatus.UnderReview, cancellationToken);
        await AddSupervisionCaseAsync(dbContext,
            Guid.Parse("00000000-0000-0000-0000-000000000304"), "DEMO-2026-0004",
            clients[1].Id, users[1].Id, "Aguardando informações para concluir a contestação.",
            nowUtc.AddDays(-10), nowUtc.AddHours(-12), RequestStatus.WaitingForCustomer, cancellationToken);
        await AddSupervisionCaseAsync(dbContext,
            Guid.Parse("00000000-0000-0000-0000-000000000305"), "DEMO-2026-0005",
            clients[1].Id, users[0].Id, "Contestação encerrada para revisão da supervisão.",
            nowUtc.AddDays(-10), nowUtc.AddHours(-12), RequestStatus.Resolved, cancellationToken);
    }

    private static async Task AddSupervisionCaseAsync(DemoDbContext dbContext, Guid requestId,
        string protocol, Guid clientId, Guid attendantId, string description,
        DateTimeOffset openedAtUtc, DateTimeOffset deadlineAtUtc, RequestStatus finalStatus,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Requests.AnyAsync(request => request.Id == requestId, cancellationToken))
            return;
        var client = await dbContext.Clients.SingleAsync(item => item.Id == clientId, cancellationToken);
        var attendant = await dbContext.Users.SingleAsync(item => item.Id == attendantId, cancellationToken);
        var request = ServiceRequest.OpenFeeDispute(requestId, protocol, client, attendant,
            description, $"seed-{requestId}", new FixedTimeProvider(openedAtUtc));
        request.ChangeDeadline(deadlineAtUtc, attendant, "Prazo ajustado no cenário fictício",
            new FixedTimeProvider(openedAtUtc.AddHours(1)));
        request.ChangeStatus(RequestStatus.UnderReview, attendant, "Análise do cenário fictício",
            new FixedTimeProvider(openedAtUtc.AddHours(2)));
        if (finalStatus != RequestStatus.UnderReview)
            request.ChangeStatus(finalStatus, attendant, "Atualização do cenário fictício",
                new FixedTimeProvider(openedAtUtc.AddHours(3)));
        dbContext.Requests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task AddPreviousCaseAsync(DemoDbContext dbContext, Guid requestId,
        string protocol, Guid clientId, Guid attendantId, string description,
        DateTimeOffset openedAtUtc,
        IReadOnlyList<(Guid Id, Guid AuthorId, string Text, DateTimeOffset AtUtc)> contacts,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Requests.AnyAsync(request => request.Id == requestId, cancellationToken))
            return;

        var client = await dbContext.Clients.SingleAsync(item => item.Id == clientId, cancellationToken);
        var attendant = await dbContext.Users.SingleAsync(item => item.Id == attendantId, cancellationToken);
        var request = ServiceRequest.OpenFeeDispute(requestId, protocol, client, attendant,
            description, $"seed-{requestId}", new FixedTimeProvider(openedAtUtc));

        foreach (var contact in contacts)
        {
            var author = await dbContext.Users.SingleAsync(item => item.Id == contact.AuthorId, cancellationToken);
            request.AddInteraction(contact.Id, author, contact.Text, new FixedTimeProvider(contact.AtUtc));
        }

        dbContext.Requests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}
