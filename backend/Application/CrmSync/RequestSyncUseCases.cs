using Application.Requests;

namespace Application.CrmSync;

public sealed record SyncMessage(Guid EventId, string CorrelationId, long RequestVersion, string EventType, string Status,
    int AttemptCount, DateTimeOffset? LastAttemptAtUtc, DateTimeOffset? NextAttemptAtUtc,
    DateTimeOffset? SyncedAtUtc, string? LastError);
public sealed record RequestSyncState(Guid RequestId, string Status, IReadOnlyList<SyncMessage> Messages);

public sealed class NoFailedSyncException : Exception;

public interface IRequestSyncRepository
{
    Task<RequestSyncState?> FindAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<int> RetryFailedAsync(Guid requestId, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

public sealed class GetRequestSyncUseCase(IRequestSyncRepository repository)
{
    public Task<RequestSyncState?> ExecuteAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        repository.FindAsync(requestId, cancellationToken);
}

public sealed class RetryRequestSyncUseCase(IRequestSyncRepository repository, TimeProvider clock)
{
    public async Task<RequestSyncState> ExecuteAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        if (await repository.FindAsync(requestId, cancellationToken) is null)
            throw new RequestNotFoundException();
        if (await repository.RetryFailedAsync(requestId, clock.GetUtcNow(), cancellationToken) == 0)
            throw new NoFailedSyncException();
        return await repository.FindAsync(requestId, cancellationToken)
            ?? throw new RequestNotFoundException();
    }
}

public interface ICrmControlGateway
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<bool> SetAvailableAsync(bool available, CancellationToken cancellationToken = default);
}

public sealed class ManageCrmDemoUseCase(ICrmControlGateway gateway)
{
    public Task<bool> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        gateway.IsAvailableAsync(cancellationToken);

    public Task<bool> SetAvailabilityAsync(bool available, CancellationToken cancellationToken = default) =>
        gateway.SetAvailableAsync(available, cancellationToken);
}
