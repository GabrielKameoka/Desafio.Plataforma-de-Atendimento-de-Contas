namespace Application.Diagnostics;

public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> CurrentValue = new();
    public static string? Current => CurrentValue.Value;

    public static IDisposable Begin(string correlationId)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = correlationId;
        return new Reset(() => CurrentValue.Value = previous);
    }

    public static string CurrentOrNew() => CurrentValue.Value ?? Guid.NewGuid().ToString("N");

    private sealed class Reset(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
