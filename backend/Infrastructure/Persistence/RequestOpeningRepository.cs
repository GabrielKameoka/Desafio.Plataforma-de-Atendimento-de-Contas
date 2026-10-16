using System.Text.Json;
using System.Diagnostics;
using Application.Diagnostics;
using Application.Requests;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Persistence;

public sealed class RequestOpeningRepository(DemoDbContext dbContext,
    DemoDiagnostics? diagnostics = null, ILogger<RequestOpeningRepository>? logger = null)
    : IRequestOpeningRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StoredOpening?> FindByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        return record is null ? null : Deserialize(record);
    }

    public Task<Client?> FindClientAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Clients.SingleOrDefaultAsync(client => client.Id == id, cancellationToken);

    public Task<DemoUser?> FindAttendantAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Users.SingleOrDefaultAsync(user => user.Id == id && user.IsActive &&
            user.Role == DemoRole.Attendant, cancellationToken);

    public async Task<StoredOpening> CreateOrGetAsync(ServiceRequest request, string key,
        string requestHash, OpenRequestResponse response, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var eventId = Guid.NewGuid();
        var correlationId = CorrelationContext.CurrentOrNew();
        var eventPayload = JsonSerializer.Serialize(new
        {
            EventId = eventId,
            CorrelationId = correlationId,
            request.Protocol,
            request.ClientId,
            EventType = "RequestOpened",
            RequestVersion = request.Version,
            Status = request.Status.ToString(),
            request.AssigneeId,
            OccurredAtUtc = request.OpenedAtUtc
        }, JsonOptions);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            dbContext.Requests.Add(request);
            dbContext.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = key,
                RequestHash = requestHash,
                RequestId = request.Id,
                Protocol = request.Protocol,
                ResponseJson = JsonSerializer.Serialize(response, JsonOptions),
                CreatedAtUtc = request.OpenedAtUtc
            });
            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                EventId = eventId,
                RequestId = request.Id,
                RequestVersion = request.Version,
                CorrelationId = correlationId,
                EventType = "RequestOpened",
                Payload = eventPayload,
                CreatedAtUtc = request.OpenedAtUtc,
                NextAttemptAtUtc = request.OpenedAtUtc
            });

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                diagnostics?.RecordCaseOpened();
                diagnostics?.RecordOperation("open", true, started.Elapsed.TotalMilliseconds);
                logger?.LogInformation("Case opened: {RequestId} {EventId} {CorrelationId} {DurationMs}",
                    request.Id, eventId, correlationId, started.Elapsed.TotalMilliseconds);
                return new StoredOpening(requestHash, response, true);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_idempotency_records" })
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }
        }

        diagnostics?.RecordOperation("open", true, started.Elapsed.TotalMilliseconds);
        logger?.LogInformation("Case opening replayed: {RequestId} {CorrelationId} {DurationMs}",
            request.Id, correlationId, started.Elapsed.TotalMilliseconds);
        return await FindByKeyAsync(key, cancellationToken)
            ?? throw new InvalidOperationException("A chave idempotente concorrente não pôde ser recuperada.");
    }

    private static StoredOpening Deserialize(IdempotencyRecord record) =>
        new(record.RequestHash,
            JsonSerializer.Deserialize<OpenRequestResponse>(record.ResponseJson, JsonOptions)
                ?? throw new InvalidOperationException("Resposta idempotente não encontrada."),
            false);
}
