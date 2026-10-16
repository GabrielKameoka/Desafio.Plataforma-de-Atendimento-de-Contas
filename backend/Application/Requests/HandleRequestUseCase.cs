using Domain;

namespace Application.Requests;

public sealed record RequestInteraction(Guid Id, Guid AuthorId, string AuthorName, string Text, DateTimeOffset OccurredAtUtc);
public sealed record RequestAudit(Guid Id, string Type, string? PreviousValue, string? NewValue,
    Guid AuthorId, string AuthorName, string Reason, DateTimeOffset OccurredAtUtc, long ResultingVersion);
public sealed record RequestDetail(Guid Id, string Protocol, Guid ClientId, string ClientName,
    string Type, string Description, string Status, string Priority, Guid AssigneeId,
    string AssigneeName, DateTimeOffset OpenedAtUtc, DateTimeOffset DeadlineAtUtc, long Version,
    string? SyncStatus, IReadOnlyList<RequestInteraction> Interactions, IReadOnlyList<RequestAudit> AuditEvents);

public sealed record RequestSaveResult(bool Saved, long? CurrentVersion);
public sealed class RequestNotFoundException : Exception;
public sealed class SupervisorUnavailableException : Exception;
public sealed class RequestVersionConflictException(Guid requestId, long currentVersion) : Exception
{
    public Guid RequestId { get; } = requestId;
    public long CurrentVersion { get; } = currentVersion;
}

public interface IRequestHandlingRepository
{
    Task<RequestDetail?> FindDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ServiceRequest?> FindRequestAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DemoUser?> FindAttendantAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DemoUser?> FindSupervisorAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RequestSaveResult> SaveAsync(ServiceRequest request, long expectedVersion,
        AuditEvent? audit, Interaction? interaction, string? outboxEventType,
        CancellationToken cancellationToken = default);
}

public sealed class GetRequestDetailUseCase(IRequestHandlingRepository repository)
{
    public Task<RequestDetail?> ExecuteAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.FindDetailAsync(id, cancellationToken);
}

public sealed class HandleRequestUseCase(IRequestHandlingRepository repository, TimeProvider clock)
{
    public async Task<RequestDetail> ReopenAsync(Guid id, long version, Guid supervisorId,
        string? reason, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty || version < 1)
            throw new DomainRuleException("invalid_version", "Informe o identificador e a versão exibida do caso.");
        var request = await repository.FindRequestAsync(id, cancellationToken)
            ?? throw new RequestNotFoundException();
        if (request.Version != version)
            throw new RequestVersionConflictException(id, request.Version);
        var supervisor = await repository.FindSupervisorAsync(supervisorId, cancellationToken)
            ?? throw new SupervisorUnavailableException();
        request.Reopen(supervisor, reason, clock);
        var audit = request.AuditEvents.Single(item => item.ResultingVersion == request.Version);
        var saved = await repository.SaveAsync(request, version, audit, null,
            "RequestStatusChanged", cancellationToken);
        if (!saved.Saved)
            throw saved.CurrentVersion is { } current
                ? new RequestVersionConflictException(id, current)
                : new RequestNotFoundException();
        return await repository.FindDetailAsync(id, cancellationToken)
            ?? throw new RequestNotFoundException();
    }

    public Task<RequestDetail> AddInteractionAsync(Guid id, long version, Guid actorId, string? text,
        CancellationToken cancellationToken = default) => ChangeAsync(id, version, actorId,
            (request, actor) => request.AddInteraction(Guid.NewGuid(), actor, text, clock), null, cancellationToken);

    public Task<RequestDetail> ChangeStatusAsync(Guid id, long version, Guid actorId, RequestStatus status,
        string? reason, CancellationToken cancellationToken = default) => ChangeAsync(id, version, actorId,
            (request, actor) => { request.ChangeStatus(status, actor, reason, clock); return null; },
            "RequestStatusChanged", cancellationToken);

    public Task<RequestDetail> ChangePriorityAsync(Guid id, long version, Guid actorId, RequestPriority priority,
        string? reason, CancellationToken cancellationToken = default) => ChangeAsync(id, version, actorId,
            (request, actor) => { request.ChangePriority(priority, actor, reason, clock); return null; },
            null, cancellationToken);

    public Task<RequestDetail> ChangeDeadlineAsync(Guid id, long version, Guid actorId, DateTimeOffset deadlineAtUtc,
        string? reason, CancellationToken cancellationToken = default) => ChangeAsync(id, version, actorId,
            (request, actor) => { request.ChangeDeadline(deadlineAtUtc, actor, reason, clock); return null; },
            null, cancellationToken);

    public async Task<RequestDetail> AssignAsync(Guid id, long version, Guid actorId, Guid assigneeId,
        string? reason, CancellationToken cancellationToken = default)
    {
        var assignee = await repository.FindAttendantAsync(assigneeId, cancellationToken);
        return await ChangeAsync(id, version, actorId,
            (request, actor) => { request.AssignTo(assignee, actor, reason, clock); return null; },
            "RequestAssigneeChanged", cancellationToken);
    }

    private async Task<RequestDetail> ChangeAsync(Guid id, long version, Guid actorId,
        Func<ServiceRequest, DemoUser, Interaction?> change, string? outboxEventType,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || version < 1)
            throw new DomainRuleException("invalid_version", "Informe o identificador e a versão exibida do caso.");
        var request = await repository.FindRequestAsync(id, cancellationToken)
            ?? throw new RequestNotFoundException();
        if (request.Version != version)
            throw new RequestVersionConflictException(id, request.Version);
        var actor = await repository.FindAttendantAsync(actorId, cancellationToken)
            ?? throw new AttendantUnavailableException();

        var interaction = change(request, actor);
        var audit = request.AuditEvents.SingleOrDefault(item => item.ResultingVersion == request.Version);
        var saved = await repository.SaveAsync(request, version, audit, interaction, outboxEventType,
            cancellationToken);
        if (!saved.Saved)
            throw saved.CurrentVersion is { } current
                ? new RequestVersionConflictException(id, current)
                : new RequestNotFoundException();
        return await repository.FindDetailAsync(id, cancellationToken)
            ?? throw new RequestNotFoundException();
    }
}
