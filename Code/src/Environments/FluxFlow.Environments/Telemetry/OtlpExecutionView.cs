using System.Text.Json;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;

namespace FluxFlow.Environments.Telemetry;

// Display projections only. The retained and transported telemetry is unmodified OTLP.
public static class OtlpExecutionView
{
    public static ExecutionLogSnapshot Logs(byte[] content, long dropped)
    {
        List<ExecutionLog> records = [];
        foreach (var resource in ExportLogsServiceRequest.Parser.ParseFrom(content).ResourceLogs)
        foreach (var scope in resource.ScopeLogs)
        foreach (var record in scope.LogRecords)
        {
            var severity = (int)record.SeverityNumber;
            var level = severity is >= 1 and <= 24
                ? new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical" }[(severity - 1) / 4]
                : record.SeverityText;
            // The SDK emits original event/exception details before structured state and scope attributes.
            // Later attributes with the same names remain visible without replacing those original details.
            var eventId = record.Attributes.FirstOrDefault(item => item.Key == "logrecord.event.id")?.Value.IntValue ?? 0;
            var exception = record.Attributes.FirstOrDefault(item => item.Key == "exception.stacktrace")?.Value.StringValue;
            var body = record.Body?.StringValue ?? "";
            records.Add(new(dropped + records.Count + 1, Time(record.TimeUnixNano), level, scope.Scope?.Name ?? "",
                eventId is >= int.MinValue and <= int.MaxValue ? (int)eventId : 0, Empty(record.EventName), body, exception,
                new(Time(record.ObservedTimeUnixNano), severity, record.SeverityText, body,
                    Id(record.TraceId), Id(record.SpanId), (int)(record.Flags & 255),
                    Attributes(record.Attributes), Attributes(resource.Resource?.Attributes ?? []))));
        }
        return new(records, dropped);
    }

    public static ExecutionTraceSnapshot Traces(byte[] content, long dropped)
    {
        List<ExecutionTrace> records = [];
        foreach (var resource in ExportTraceServiceRequest.Parser.ParseFrom(content).ResourceSpans)
        foreach (var scope in resource.ScopeSpans)
        foreach (var span in scope.Spans)
            records.Add(new(dropped + records.Count + 1, span.Name, scope.Scope?.Name ?? "", Empty(scope.Scope?.Version),
                Id(span.TraceId) ?? "", Id(span.SpanId) ?? "", Id(span.ParentSpanId), Empty(span.TraceState), (int)(span.Flags & 255),
                span.Kind.ToString(), Time(span.StartTimeUnixNano), TimeSpan.FromTicks((long)((span.EndTimeUnixNano - span.StartTimeUnixNano) / 100)),
                span.Status?.Code.ToString() ?? "Unset", Empty(span.Status?.Message), Dictionary(span.Attributes),
                span.Events.Select(item => new ExecutionTraceEvent(item.Name, Time(item.TimeUnixNano), Dictionary(item.Attributes))).ToArray(),
                span.Links.Select(item => new ExecutionTraceLink(Id(item.TraceId) ?? "", Id(item.SpanId) ?? "", Empty(item.TraceState),
                    (int)(item.Flags & 255), (item.Flags & 512) != 0, Dictionary(item.Attributes))).ToArray(),
                Dictionary(resource.Resource?.Attributes ?? [])));
        return new(records, dropped);
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static string? Id(ByteString value) => value.IsEmpty ? null : Convert.ToHexString(value.Span).ToLowerInvariant();
    private static DateTimeOffset Time(ulong nanoseconds) => DateTimeOffset.UnixEpoch.AddTicks(checked((long)(nanoseconds / 100)));
    private static ExecutionLogAttribute[] Attributes(IEnumerable<KeyValue> values) =>
        values.Select(item => new ExecutionLogAttribute(item.Key, Value(item.Value))).ToArray();
    private static IReadOnlyDictionary<string, JsonElement> Dictionary(IEnumerable<KeyValue> values)
    {
        Dictionary<string, JsonElement> result = new(StringComparer.Ordinal);
        foreach (var item in values) result[item.Key] = Value(item.Value);
        return result;
    }

    private static JsonElement Value(AnyValue value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, value);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void Write(Utf8JsonWriter writer, AnyValue value)
    {
        switch (value.ValueCase)
        {
            case AnyValue.ValueOneofCase.StringValue: writer.WriteStringValue(value.StringValue); break;
            case AnyValue.ValueOneofCase.BoolValue: writer.WriteBooleanValue(value.BoolValue); break;
            case AnyValue.ValueOneofCase.IntValue: writer.WriteNumberValue(value.IntValue); break;
            case AnyValue.ValueOneofCase.DoubleValue:
                if (double.IsFinite(value.DoubleValue)) writer.WriteNumberValue(value.DoubleValue);
                else writer.WriteStringValue(value.DoubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case AnyValue.ValueOneofCase.BytesValue: writer.WriteBase64StringValue(value.BytesValue.Span); break;
            case AnyValue.ValueOneofCase.ArrayValue:
                writer.WriteStartArray();
                foreach (var item in value.ArrayValue.Values) Write(writer, item);
                writer.WriteEndArray();
                break;
            case AnyValue.ValueOneofCase.KvlistValue:
                writer.WriteStartObject();
                foreach (var item in value.KvlistValue.Values) { writer.WritePropertyName(item.Key); Write(writer, item.Value); }
                writer.WriteEndObject();
                break;
            default: writer.WriteNullValue(); break;
        }
    }
}
