using FluxFlow.Environments;
using FluxFlow.Environments.Telemetry;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace FluxFlow.Execution;

public sealed class ExecutionTraceBuffer
{
    public const int Capacity = 10_000;
    private readonly ExecutionOtlpBuffer _buffer;
    internal Guid? RunId { get; }
    public ExecutionTraceBuffer() : this(null) { }
    internal ExecutionTraceBuffer(Guid? runId) => (RunId, _buffer) = (runId, new(Capacity));
    internal void Append(byte[] export) => _buffer.Append(export);

    public (byte[] Content, long Dropped) Export()
    {
        var snapshot = _buffer.Snapshot();
        var request = new ExportTraceServiceRequest();
        foreach (var export in snapshot.Exports)
            request.ResourceSpans.Add(ExportTraceServiceRequest.Parser.ParseFrom(export).ResourceSpans);
        return (request.ToByteArray(), snapshot.Dropped);
    }

    public ExecutionTraceSnapshot Snapshot()
    {
        var export = Export();
        return OtlpExecutionView.Traces(export.Content, export.Dropped);
    }
    public void Complete() => _buffer.Complete();
}
