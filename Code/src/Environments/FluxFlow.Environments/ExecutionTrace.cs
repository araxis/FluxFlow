using System.Text.Json;

namespace FluxFlow.Environments;

public sealed record ExecutionTrace(long Sequence, string Name, string Source, string? SourceVersion,
    string TraceId, string SpanId, string? ParentSpanId, string? TraceState, int TraceFlags,
    string Kind, DateTimeOffset StartTime, TimeSpan Duration, string Status, string? StatusDescription,
    IReadOnlyDictionary<string, JsonElement> Attributes, IReadOnlyList<ExecutionTraceEvent> Events,
    IReadOnlyList<ExecutionTraceLink> Links, IReadOnlyDictionary<string, JsonElement> Resource);

public sealed record ExecutionTraceEvent(string Name, DateTimeOffset Timestamp, IReadOnlyDictionary<string, JsonElement> Attributes);
public sealed record ExecutionTraceLink(string TraceId, string SpanId, string? TraceState, int TraceFlags,
    bool IsRemote, IReadOnlyDictionary<string, JsonElement> Attributes);
public sealed record ExecutionTraceSnapshot(IReadOnlyList<ExecutionTrace> Records, long Dropped);
