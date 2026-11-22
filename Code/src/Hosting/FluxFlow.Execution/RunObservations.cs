using System.Text.Json;
using FluxFlow.Environments;

namespace FluxFlow.Execution;

public sealed class RunObservations
{
    public RunObservations() { }
    internal RunObservations(Guid id)
    {
        Logs = new(id, "engine");
        Diagnostics = new(id, "runner");
        Traces = new(id);
    }
    public ExecutionLogBuffer Logs { get; } = new();
    public ExecutionLogBuffer Diagnostics { get; } = new();
    public ExecutionOutputBuffer Outputs { get; } = new();
    public ExecutionTraceBuffer Traces { get; } = new();
    public void Complete() { Logs.Complete(); Diagnostics.Complete(); Outputs.Complete(); Traces.Complete(); }
}

public sealed class ExecutionOutputBuffer
{
    public const int Capacity = 1000;
    private readonly object _gate = new();
    private readonly Queue<ExecutionOutput> _records = new();
    private long _sequence, _dropped;
    private bool _completed;
    public void Append(string port, JsonElement? message, string? error = null, string? instanceId = null)
    {
        lock (_gate)
        {
            if (_completed) return;
            _records.Enqueue(new(++_sequence, DateTimeOffset.UtcNow, port, message?.Clone(), error) { InstanceId = instanceId });
            if (_records.Count > Capacity) { _records.Dequeue(); _dropped++; }
        }
    }
    public void AddDropped(long count) { lock (_gate) { if (!_completed) _dropped += count; } }
    public ExecutionOutputSnapshot Snapshot() { lock (_gate) return new(_records.ToArray(), _dropped); }
    public void Complete() { lock (_gate) _completed = true; }
}
