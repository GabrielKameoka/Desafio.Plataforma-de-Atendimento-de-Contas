using System.Text.Json;
using System.Diagnostics;
using Application.Diagnostics;
using Application.Requests;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

public sealed class RequestHandlingRepository(DemoDbContext db, DemoDiagnostics? diagnostics = null,
    ILogger<RequestHandlingRepository>? logger = null) : IRequestHandlingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ServiceRequest?> FindRequestAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Requests.AsNoTracking().SingleOrDefaultAsync(request => request.Id == id, cancellationToken);

    public Task<DemoUser?> FindAttendantAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id && user.IsActive &&
            user.Role == DemoRole.Attendant, cancellationToken);

    public Task<DemoUser?> FindSupervisorAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id && user.IsActive &&
            user.Role == DemoRole.Supervisor, cancellationToken);

    public async Task<RequestDetail?> FindDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await db.Requests.AsNoTracking().Include(item => item.Client)
            .Include(item => item.Assignee).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (request is null)
            return null;

        var interactions = await db.Interactions.AsNoTracking().Where(item => item.RequestId == id)
            .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id)
            .Select(item => new RequestInteraction(item.Id, item.AuthorId, item.Author.DisplayName,
                item.Text, item.CreatedAtUtc)).ToListAsync(cancellationToken);
        var audits = await db.AuditEvents.AsNoTracking().Where(item => item.RequestId == id)
            .OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id)
            .Select(item => new RequestAudit(item.Id, item.Type.ToString(), item.PreviousValue,
                item.NewValue, item.AuthorId, item.Author.DisplayName, item.Reason,
                item.OccurredAtUtc, item.ResultingVersion)).ToListAsync(cancellationToken);
        var syncStates = await db.OutboxMessages.AsNoTracking().Where(item => item.RequestId == id)
            .Select(item => item.Status).ToListAsync(cancellationToken);
        var syncStatus = syncStates.Contains(OutboxStatus.Failed) ? "Failed" :
            syncStates.Contains(OutboxStatus.Processing) ? "Processing" :
            syncStates.Contains(OutboxStatus.Pending) ? "Pending" :
            syncStates.Count > 0 ? "Synced" : null;

        return new RequestDetail(request.Id, request.Protocol, request.ClientId, request.Client.DisplayName,
            request.Type.ToString(), request.Description, request.Status.ToString(), request.Priority.ToString(),
            request.AssigneeId, request.Assignee.DisplayName, request.OpenedAtUtc, request.DeadlineAtUtc,
            request.Version, syncStatus, interactions, audits);
    }

    public async Task<RequestSaveResult> SaveAsync(ServiceRequest request, long expectedVersion,
        AuditEvent? audit, Interaction? interaction, string? outboxEventType,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var correlationId = CorrelationContext.CurrentOrNew();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await db.Requests.Where(item => item.Id == request.Id && item.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Version, request.Version)
                .SetProperty(item => item.Status, request.Status)
                .SetProperty(item => item.Priority, request.Priority)
                .SetProperty(item => item.AssigneeId, request.AssigneeId)
                .SetProperty(item => item.DeadlineAtUtc, request.DeadlineAtUtc), cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var currentVersion = await db.Requests.AsNoTracking().Where(item => item.Id == request.Id)
                .Select(item => (long?)item.Version).SingleOrDefaultAsync(cancellationToken);
            diagnostics?.RecordOperation("change", false, started.Elapsed.TotalMilliseconds);
            logger?.LogWarning("Case version conflict: {RequestId} {CorrelationId} {DurationMs}",
                request.Id, correlationId, started.Elapsed.TotalMilliseconds);
            return new RequestSaveResult(false, currentVersion);
        }

        if (audit is not null)
            db.AuditEvents.Add(audit);
        if (interaction is not null)
            db.Interactions.Add(interaction);
        if (outboxEventType is not null)
        {
            var eventId = Guid.NewGuid();
            var occurredAtUtc = audit?.OccurredAtUtc ?? throw new InvalidOperationException("Evento de integração exige auditoria.");
            db.OutboxMessages.Add(new OutboxMessage
            {
                EventId = eventId,
                RequestId = request.Id,
                RequestVersion = request.Version,
                CorrelationId = correlationId,
                EventType = outboxEventType,
                Payload = JsonSerializer.Serialize(new
                {
                    EventId = eventId,
                    CorrelationId = correlationId,
                    request.Protocol,
                    request.ClientId,
                    EventType = outboxEventType,
                    RequestVersion = request.Version,
                    Status = request.Status.ToString(),
                    request.AssigneeId,
                    OccurredAtUtc = occurredAtUtc
                }, JsonOptions),
                CreatedAtUtc = occurredAtUtc,
                NextAttemptAtUtc = occurredAtUtc
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        diagnostics?.RecordOperation("change", true, started.Elapsed.TotalMilliseconds);
        logger?.LogInformation("Case changed: {RequestId} {CorrelationId} {Operation} {DurationMs}",
            request.Id, correlationId, audit?.Type.ToString() ?? "Interaction",
            started.Elapsed.TotalMilliseconds);
        return new RequestSaveResult(true, null);
    }
}
