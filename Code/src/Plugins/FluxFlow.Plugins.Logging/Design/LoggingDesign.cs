using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.Logger;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Environments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Plugins.Logging;

public sealed class LoggingDesign : IPluginDesign
{
    public static InfrastructureServiceType Collector { get; } = new("logging.otlp", "OTLP logging",
        "http://localhost:4318/v1/logs", "HTTP or HTTPS collector endpoint.",
        uri => OtlpSettingsValidation.EndpointError(uri.AbsoluteUri) is null);

    public void ConfigureRemoteServices(IServiceCollection services) =>
        services.AddSingleton<IResourceEnvironmentAdapter>(new EndpointEnvironmentAdapter(Collector));

    public PropertyRules Component(PluginComponent component) => new()
    {
        TextValidators = new LoggerDescription().Inspector.Sections.SelectMany(s => s.Options)
            .Where(o => o.ValidateText is not null).ToDictionary(o => o.Name, o => o.ValidateText!),
        Validate = settings =>
        {
            var config = settings is IConfigurationSection section ? section.ReadRequiredConfiguration<LoggerConfiguration>()
                : settings.Get<LoggerConfiguration>() ?? throw new ArgumentException("Missing logger settings.");
            config.Validate();
        }
    };

    public PropertyRules Connection(PluginConnection connection) => new()
    {
        Validate = OtlpSettingsValidation.Validate,
        Groups = _ => [
            new("Collector", null, [
                Create(nameof(CollectorOptions.Endpoint), "Endpoint", help: "Absolute HTTP(S) collector address without credentials or a fragment.") with
                    { IsRequired = true, ValidateText = OtlpSettingsValidation.EndpointError },
                Create(nameof(CollectorOptions.Protocol), "Protocol", OptionEditorKind.Select, defaultValue: nameof(CollectorProtocol.HttpProtobuf)) with
                {
                    Choices = [
                        new() { Value = nameof(CollectorProtocol.HttpProtobuf), DisplayName = "HTTP/protobuf" },
                        new() { Value = nameof(CollectorProtocol.Grpc), DisplayName = "gRPC" }
                    ]
                },
                Create(nameof(CollectorOptions.ExportTimeout), "Timeout", OptionEditorKind.Duration,
                    "Maximum export wait, from one millisecond to Int32.MaxValue milliseconds.", "00:00:10") with
                    { MinDuration = TimeSpan.FromMilliseconds(1), MaxDuration = TimeSpan.FromMilliseconds(int.MaxValue) }
            ])
            {
                StringMaps = [new(nameof(CollectorOptions.Headers), "Headers", "header")
                {
                    Comparer = StringComparer.OrdinalIgnoreCase, SecretValues = true,
                    ReadEntries = OtlpHeaders.Read, WriteEntries = OtlpHeaders.Write, Validate = OtlpHeaders.Validate,
                    ValidateJson = OtlpHeaders.ValidateJson
                }]
            },
            new("Logging", null, [
                Create(nameof(CollectorOptions.ServiceName), "Service name", help: "Optional service.name attribute. Omit to leave it unset."),
                Create(nameof(CollectorOptions.MinimumLogLevel), "Minimum level", OptionEditorKind.Select, defaultValue: nameof(LogLevel.Trace)) with
                {
                    Choices = Enum.GetNames<LogLevel>().Select(value => new OptionChoiceModel { Value = value, DisplayName = value }).ToArray()
                },
                Create(nameof(CollectorOptions.ReportExportErrors), "Report failures", OptionEditorKind.Toggle,
                    "Use the existing exporter failure reporting behavior.", false)
            ])
        ]
    };
}
