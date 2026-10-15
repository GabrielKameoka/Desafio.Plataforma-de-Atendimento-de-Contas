namespace Infrastructure.Persistence;

public sealed class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
