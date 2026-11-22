using System.Text.Json;

namespace FluxFlow.Environments;

public sealed record ExecutionOutput(long Sequence, DateTimeOffset Timestamp, string Port, JsonElement? Message, string? Error)
{
    public string? InstanceId { get; init; }
}
public sealed record ExecutionOutputSnapshot(IReadOnlyList<ExecutionOutput> Records, long Dropped);
