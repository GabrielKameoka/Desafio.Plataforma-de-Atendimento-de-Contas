using Application.Demo;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class RequestOverviewRepository(DemoDbContext dbContext) : IRequestOverviewRepository
{
    public Task<int> CountRequestsAsync(CancellationToken cancellationToken = default) =>
        dbContext.Requests.AsNoTracking().CountAsync(cancellationToken);
}
