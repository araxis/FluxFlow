using System.Text.Json;

namespace FluxFlow.Environments;

public sealed record ExecutionLog(long Sequence, DateTimeOffset Timestamp, string Level, string Category,
    int EventId, string? EventName, string Message, string? Exception, ExecutionLogTelemetry? Telemetry = null);

public sealed record ExecutionLogAttribute(string Name, JsonElement Value);

public sealed record ExecutionLogTelemetry(DateTimeOffset ObservedTimestamp, int SeverityNumber, string? SeverityText,
    string? Body, string? TraceId, string? SpanId, int TraceFlags,
    IReadOnlyList<ExecutionLogAttribute> Attributes,
    IReadOnlyList<ExecutionLogAttribute> Resource);

public sealed record ExecutionLogSnapshot(IReadOnlyList<ExecutionLog> Records, long Dropped);
