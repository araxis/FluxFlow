using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace FluxFlow.Components.Logging;

public static class OtlpWorkflowExtensions
{
    public static IServiceCollection AddOtlpLogger(this IServiceCollection services, string path, string endpointText,
        OtlpExportProtocol? exportProtocol = null, string? headers = null, string? serviceName = null,
        TimeSpan? exportTimeout = null, bool? reportErrors = null, LogLevel? minimumLevel = null)
    {
        var protocol = exportProtocol ?? OtlpExportProtocol.HttpProtobuf;
        var timeout = exportTimeout ?? TimeSpan.FromSeconds(10);
        var minimumLogLevel = minimumLevel ?? LogLevel.Trace;
        var reportExportErrors = reportErrors ?? false;
        headers ??= string.Empty;
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https")
            || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("Endpoint must be an absolute HTTP(S) address without credentials or a fragment.");
        if (!Enum.IsDefined(protocol)) throw new ArgumentOutOfRangeException(nameof(exportProtocol));
        if (!Enum.IsDefined(minimumLogLevel)) throw new ArgumentOutOfRangeException(nameof(minimumLevel));
        if (timeout.TotalMilliseconds < 1 || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(exportTimeout));
        // OTLP headers use comma-separated key=value pairs. Values may themselves contain '='.
        using var headerValidation = new HttpRequestMessage();
        foreach (var header in headers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = header.IndexOf('=');
            if (separator <= 0 || string.IsNullOrWhiteSpace(header[..separator]) || header.Contains('\r') || header.Contains('\n'))
                throw new ArgumentException("Headers must contain comma-separated key=value entries.");
            headerValidation.Headers.Add(Uri.UnescapeDataString(header[..separator].Trim()), Uri.UnescapeDataString(header[(separator + 1)..].Trim()));
        }
        return services.AddFluxFlowResource<ILoggerFactory>(ApplicationAddress.Parse(path), _ =>
        {
            var exporter = new OtlpExporterOptions
            {
                Endpoint = endpoint, Protocol = protocol, Headers = headers,
                TimeoutMilliseconds = (int)timeout.TotalMilliseconds
            };
            var resource = ResourceBuilder.CreateEmpty();
            if (!string.IsNullOrWhiteSpace(serviceName))
                resource.AddAttributes([new KeyValuePair<string, object>("service.name", serviceName)]);
            return reportExportErrors
                ? new ExportReportingLoggerFactory(new OtlpLogExporter(exporter), resource, minimumLogLevel)
                : LoggerFactory.Create(builder => builder.SetMinimumLevel(minimumLogLevel).AddOpenTelemetry(options =>
                {
                    options.SetResourceBuilder(resource);
                    options.IncludeScopes = true;
                    options.AddOtlpExporter(settings =>
                    {
                        settings.Endpoint = exporter.Endpoint; settings.Protocol = exporter.Protocol;
                        settings.Headers = exporter.Headers; settings.TimeoutMilliseconds = exporter.TimeoutMilliseconds;
                    });
                }));
        });
    }
}
