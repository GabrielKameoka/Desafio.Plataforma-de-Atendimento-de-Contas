namespace Infrastructure.Persistence;

public enum OutboxStatus { Pending, Processing, Failed, Synced }

public sealed class OutboxMessage
{
    public Guid EventId { get; set; }
    public Guid RequestId { get; set; }
    public long RequestVersion { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset? SyncedAtUtc { get; set; }
    public string? LastError { get; set; }
}
