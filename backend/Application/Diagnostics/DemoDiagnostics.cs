using System.Diagnostics.Metrics;

namespace Application.Diagnostics;

public sealed record OperationMeasure(string Operation, long Count, double AverageMilliseconds,
    double LastMilliseconds);

public sealed record DiagnosticsSnapshot(long CaseCount, long PendingSyncCount,
    long FailedSyncCount, long SyncFailureAttempts, IReadOnlyList<OperationMeasure> Operations);

public interface IDiagnosticsRepository
{
    Task<(long Cases, long Pending, long Failed)> CountAsync(CancellationToken cancellationToken = default);
}

public sealed class GetDiagnosticsUseCase(IDiagnosticsRepository repository, DemoDiagnostics diagnostics)
{
    public async Task<DiagnosticsSnapshot> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var counts = await repository.CountAsync(cancellationToken);
        return new DiagnosticsSnapshot(counts.Cases, counts.Pending, counts.Failed,
            diagnostics.SyncFailureAttempts, diagnostics.Operations());
    }
}

public sealed class DemoDiagnostics
{
    public const string MeterName = "Atendimento.Demo";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> CasesOpened = Meter.CreateCounter<long>("atendimento.cases.opened");
    private static readonly Counter<long> SyncFailures = Meter.CreateCounter<long>("atendimento.crm.send.failures");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "atendimento.operation.duration", unit: "ms");
    private readonly object gate = new();
    private readonly Dictionary<string, (long Count, double Total, double Last)> operations = new();
    private long syncFailureAttempts;

    public long SyncFailureAttempts => Interlocked.Read(ref syncFailureAttempts);

    public void RecordCaseOpened() => CasesOpened.Add(1);

    public void RecordOperation(string operation, bool success, double durationMilliseconds)
    {
        // Callers use fixed names only. No customer, protocol or free text becomes a metric attribute.
        Duration.Record(durationMilliseconds, new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", success ? "success" : "failure"));
        lock (gate)
        {
            var current = operations.GetValueOrDefault(operation);
            operations[operation] = (current.Count + 1, current.Total + durationMilliseconds,
                durationMilliseconds);
        }
    }

    public void RecordSyncFailure(string eventType)
    {
        Interlocked.Increment(ref syncFailureAttempts);
        SyncFailures.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    }

    public IReadOnlyList<OperationMeasure> Operations()
    {
        lock (gate)
            return operations.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new OperationMeasure(item.Key, item.Value.Count,
                    item.Value.Total / item.Value.Count, item.Value.Last)).ToList();
    }
}
