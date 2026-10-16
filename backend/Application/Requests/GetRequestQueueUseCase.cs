using Domain;

namespace Application.Requests;

public enum DueFilter { DueSoon, Overdue }

public sealed record RequestQueueQuery(RequestStatus? Status = null, RequestPriority? Priority = null,
    Guid? AssigneeId = null, DueFilter? Due = null, int Page = 1, int PageSize = 20);

public sealed record RequestQueueItem(Guid Id, string Protocol, Guid ClientId, string ClientName,
    string Status, string Priority, Guid AssigneeId, string AssigneeName,
    DateTimeOffset DeadlineAtUtc, long Version, string DueState);

public sealed record RequestQueuePage(IReadOnlyList<RequestQueueItem> Items, int Total,
    int Page, int PageSize, DateTimeOffset EvaluatedAtUtc);

public interface IRequestQueueRepository
{
    Task<RequestQueuePage> SearchAsync(RequestQueueQuery query, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

public sealed class GetRequestQueueUseCase(IRequestQueueRepository repository, TimeProvider clock)
{
    public Task<RequestQueuePage> ExecuteAsync(RequestQueueQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.Page < 1 || query.Page > 10000 || query.PageSize < 1 || query.PageSize > 50 ||
            (query.AssigneeId.HasValue && query.AssigneeId.Value == Guid.Empty) ||
            (query.Status.HasValue && !Enum.IsDefined(query.Status.Value)) ||
            (query.Priority.HasValue && !Enum.IsDefined(query.Priority.Value)) ||
            (query.Due.HasValue && !Enum.IsDefined(query.Due.Value)))
            throw new DomainRuleException("invalid_request_filter", "Filtros e paginação devem ser válidos; use até 50 casos por página.");
        return repository.SearchAsync(query, clock.GetUtcNow(), cancellationToken);
    }
}
