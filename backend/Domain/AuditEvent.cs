namespace Domain;

public enum AuditEventType { Opened, StatusChanged, PriorityChanged, AssigneeChanged, DeadlineChanged, Reopened }

public sealed class AuditEvent
{
    private AuditEvent() { }

    public AuditEvent(Guid id, Guid requestId, AuditEventType type, string? previousValue,
        string? newValue, Guid authorId, string reason, DateTimeOffset occurredAtUtc, long resultingVersion)
    {
        Id = id;
        RequestId = requestId;
        Type = type;
        PreviousValue = previousValue;
        NewValue = newValue;
        AuthorId = authorId;
        Reason = reason;
        OccurredAtUtc = occurredAtUtc;
        ResultingVersion = resultingVersion;
    }

    public Guid Id { get; private set; }
    public Guid RequestId { get; private set; }
    public ServiceRequest Request { get; private set; } = null!;
    public AuditEventType Type { get; private set; }
    public string? PreviousValue { get; private set; }
    public string? NewValue { get; private set; }
    public Guid AuthorId { get; private set; }
    public DemoUser Author { get; private set; } = null!;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public long ResultingVersion { get; private set; }
}
