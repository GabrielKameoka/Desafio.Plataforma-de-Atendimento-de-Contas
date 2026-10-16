using System.Text;
using System.Diagnostics;
using Application.Diagnostics;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.CrmSync;

public sealed class OutboxProcessor(DemoDbContext db, HttpClient crm, TimeProvider clock,
    DemoDiagnostics? diagnostics = null, ILogger<OutboxProcessor>? logger = null)
{
    private const int BatchSize = 10;
    private const int MaxAutomaticAttempts = 3;

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        await db.OutboxMessages.Where(item => item.Status == OutboxStatus.Processing &&
                item.LastAttemptAtUtc < now.AddSeconds(-30))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, OutboxStatus.Pending)
                .SetProperty(item => item.NextAttemptAtUtc, now), cancellationToken);

        List<OutboxMessage> reserved;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            reserved = await db.OutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM outbox_messages AS m
                WHERE m."Status" = 'Pending' AND m."NextAttemptAtUtc" <= {now}
                  AND NOT EXISTS (
                    SELECT 1 FROM outbox_messages AS earlier
                    WHERE earlier."RequestId" = m."RequestId"
                      AND earlier."RequestVersion" < m."RequestVersion"
                      AND earlier."Status" <> 'Synced')
                ORDER BY m."CreatedAtUtc", m."EventId"
                LIMIT {BatchSize} FOR UPDATE OF m SKIP LOCKED
                """).ToListAsync(cancellationToken);
            foreach (var message in reserved)
            {
                message.Status = OutboxStatus.Processing;
                message.AttemptCount++;
                message.LastAttemptAtUtc = now;
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var message in reserved)
        {
            var started = Stopwatch.StartNew();
            var failure = await SendAsync(message, cancellationToken);
            diagnostics?.RecordOperation("send", failure is null, started.Elapsed.TotalMilliseconds);
            if (failure is not null)
                diagnostics?.RecordSyncFailure(message.EventType);
            if (failure is null)
                logger?.LogInformation("CRM event delivered: {RequestId} {EventId} {CorrelationId} {EventType} {DurationMs}",
                    message.RequestId, message.EventId, message.CorrelationId, message.EventType,
                    started.Elapsed.TotalMilliseconds);
            else
                logger?.LogWarning("CRM event delivery failed: {RequestId} {EventId} {CorrelationId} {EventType} {FailureCode} {DurationMs}",
                    message.RequestId, message.EventId, message.CorrelationId, message.EventType, failure,
                    started.Elapsed.TotalMilliseconds);
            var completedAt = clock.GetUtcNow();
            if (failure is null)
            {
                await db.OutboxMessages.Where(item => item.EventId == message.EventId &&
                        item.Status == OutboxStatus.Processing && item.AttemptCount == message.AttemptCount)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.Status, OutboxStatus.Synced)
                        .SetProperty(item => item.SyncedAtUtc, completedAt)
                        .SetProperty(item => item.LastError, (string?)null), cancellationToken);
            }
            else
            {
                var exhausted = message.AttemptCount >= MaxAutomaticAttempts;
                var nextAttempt = completedAt.AddSeconds(1 << message.AttemptCount);
                await db.OutboxMessages.Where(item => item.EventId == message.EventId &&
                        item.Status == OutboxStatus.Processing && item.AttemptCount == message.AttemptCount)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.Status, exhausted ? OutboxStatus.Failed : OutboxStatus.Pending)
                        .SetProperty(item => item.NextAttemptAtUtc, nextAttempt)
                        .SetProperty(item => item.LastError, failure), cancellationToken);
            }
        }
        db.ChangeTracker.Clear();
        return reserved.Count;
    }

    private async Task<string?> SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "events")
            {
                Content = new StringContent(message.Payload, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", message.CorrelationId);
            using var response = await crm.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode ? null : $"http_{(int)response.StatusCode}";
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "timeout";
        }
        catch (HttpRequestException)
        {
            return "transport_error";
        }
    }
}
