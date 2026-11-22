using FluxFlow.Environments;
using FluxFlow.Environments.Telemetry;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace FluxFlow.Execution;

public sealed class ExecutionLogBuffer
{
    public const int Capacity = ExecutionOtlpBuffer.Capacity;
    private readonly ExecutionOtlpBuffer _buffer;
    internal Guid? RunId { get; }
    internal string? Origin { get; }
    public ExecutionLogBuffer() : this(null, null) { }
    internal ExecutionLogBuffer(Guid? runId, string? origin)
        => (RunId, Origin, _buffer) = (runId, origin, new());
    internal void Append(byte[] export) => _buffer.Append(export);

    public (byte[] Content, long Dropped) Export()
    {
        var snapshot = _buffer.Snapshot();
        var request = new ExportLogsServiceRequest();
        foreach (var export in snapshot.Exports)
            request.ResourceLogs.Add(ExportLogsServiceRequest.Parser.ParseFrom(export).ResourceLogs);
        return (request.ToByteArray(), snapshot.Dropped);
    }

    public ExecutionLogSnapshot Snapshot()
    {
        var export = Export();
        return OtlpExecutionView.Logs(export.Content, export.Dropped);
    }
    public void Complete() => _buffer.Complete();
}
