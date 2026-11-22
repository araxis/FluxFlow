using OpenTelemetry.Exporter;

namespace FluxFlow.Configuration.Components.Logging;

public sealed class OtlpLoggerConfiguration : CollectorSettings
{
    public OtlpExportProtocol Protocol { get; init; } = OtlpExportProtocol.HttpProtobuf;
}
