using Microsoft.Extensions.Logging;

namespace FluxFlow.Configuration.Components.Logging;

public abstract class CollectorSettings
{
    public required string Endpoint { get; init; }
    public string Headers { get; init; } = string.Empty;
    public string? ServiceName { get; init; }
    public TimeSpan ExportTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public bool ReportExportErrors { get; init; }
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Trace;
}
