using Application.CrmSync;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.CrmSync;

public sealed class RequestSyncRepository(DemoDbContext db) : IRequestSyncRepository
{
    public async Task<RequestSyncState?> FindAsync(Guid requestId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Requests.AsNoTracking().AnyAsync(item => item.Id == requestId, cancellationToken))
            return null;
        var messages = await db.OutboxMessages.AsNoTracking().Where(item => item.RequestId == requestId)
            .OrderBy(item => item.RequestVersion).Select(item => new SyncMessage(item.EventId,
                item.CorrelationId,
                item.RequestVersion, item.EventType, item.Status.ToString(), item.AttemptCount,
                item.LastAttemptAtUtc, item.Status == OutboxStatus.Pending ? item.NextAttemptAtUtc : null,
                item.SyncedAtUtc, item.LastError)).ToListAsync(cancellationToken);
        var status = messages.Any(item => item.Status == "Failed") ? "Failed" :
            messages.Any(item => item.Status == "Processing") ? "Processing" :
            messages.Any(item => item.Status == "Pending") ? "Pending" :
            messages.Count > 0 ? "Synced" : "None";
        return new RequestSyncState(requestId, status, messages);
    }

    public Task<int> RetryFailedAsync(Guid requestId, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        db.OutboxMessages.Where(item => item.RequestId == requestId && item.Status == OutboxStatus.Failed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, OutboxStatus.Pending)
                .SetProperty(item => item.AttemptCount, 0)
                .SetProperty(item => item.NextAttemptAtUtc, nowUtc)
                .SetProperty(item => item.LastAttemptAtUtc, (DateTimeOffset?)null)
                .SetProperty(item => item.LastError, (string?)null), cancellationToken);
}
