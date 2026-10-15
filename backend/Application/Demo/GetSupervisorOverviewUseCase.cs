namespace Application.Demo;

public interface IRequestOverviewRepository
{
    Task<int> CountRequestsAsync(CancellationToken cancellationToken = default);
}

public sealed class GetSupervisorOverviewUseCase(IRequestOverviewRepository requests)
{
    public async Task<SupervisorOverview> ExecuteAsync(CancellationToken cancellationToken = default) =>
        new(await requests.CountRequestsAsync(cancellationToken));
}

public sealed record SupervisorOverview(int TotalRequests);
