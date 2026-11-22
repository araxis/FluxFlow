using OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace FluxFlow.Execution;

public sealed class ExecutionLogProcessor : SimpleLogRecordExportProcessor
{
    private readonly ExecutionOtlpCapture _capture;

    static ExecutionLogProcessor()
    {
        // Supported SDK exporter option: retain ILogger's numeric EventId in OTLP.
        System.Environment.SetEnvironmentVariable("OTEL_DOTNET_EXPERIMENTAL_OTLP_EMIT_EVENT_LOG_ATTRIBUTES", "true");
    }

    public ExecutionLogProcessor() : this(new ExecutionOtlpCapture()) { }
    private ExecutionLogProcessor(ExecutionOtlpCapture capture) : base(new OtlpLogExporter(capture.Options("logs"))) => _capture = capture;

    public override void OnEnd(LogRecord record)
    {
        ExecutionLogBuffer? buffer = null;
        record.ForEachScope((scope, _) =>
        {
            if (scope.Scope is ExecutionLogBuffer destination) buffer = destination;
        }, 0);
        if (buffer is null) return;
        // Add run metadata once from the innermost capture scope. Nested runner
        // scopes must not make an engine record belong to multiple origins/runs.
        if (buffer.RunId is { } id)
            record.Attributes = [.. record.Attributes ?? [],
                new("flow.run.id", id.ToString()), new("flow.telemetry.origin", buffer.Origin)];
        using var capture = _capture.Capture(buffer.Append);
        base.OnEnd(record);
    }
}
