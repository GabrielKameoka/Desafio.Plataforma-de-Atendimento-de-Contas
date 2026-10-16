using Application.Diagnostics;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Diagnostics;

public sealed class DiagnosticsRepository(DemoDbContext db) : IDiagnosticsRepository
{
    public async Task<(long Cases, long Pending, long Failed)> CountAsync(
        CancellationToken cancellationToken = default)
    {
        var cases = await db.Requests.LongCountAsync(cancellationToken);
        var pending = await db.OutboxMessages.LongCountAsync(item =>
            item.Status == OutboxStatus.Pending || item.Status == OutboxStatus.Processing,
            cancellationToken);
        var failed = await db.OutboxMessages.LongCountAsync(item =>
            item.Status == OutboxStatus.Failed, cancellationToken);
        return (cases, pending, failed);
    }
}
