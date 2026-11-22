using FluxFlow.Configuration.Components.Logging;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Plugins.Logging;

internal static class OtlpSettingsValidation
{
    public static void Validate(IConfiguration configuration)
    {
        CollectorOptions settings;
        try
        {
            settings = configuration.Get<CollectorOptions>()
                ?? throw new ArgumentException("Enter OTLP connection settings.");
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            // Binder errors may contain a supplied value. Never echo header credentials.
            throw new ArgumentException("Check the OTLP setting types, enum values and duration format.");
        }

        if (EndpointError(settings.Endpoint) is { } endpointError)
            throw new ArgumentException(endpointError);
        if (!Enum.IsDefined(settings.Protocol)) throw new ArgumentException("Select a supported OTLP protocol.");
        if (!Enum.IsDefined(settings.MinimumLogLevel)) throw new ArgumentException("Select a standard minimum log level.");
        if (settings.ExportTimeout.TotalMilliseconds < 1 || settings.ExportTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentException("Timeout must be between one millisecond and Int32.MaxValue milliseconds.");

        OtlpHeaders.ValidateList(settings.Headers);
    }

    public static string? EndpointError(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var endpoint) &&
        endpoint.Scheme is "http" or "https" && endpoint.UserInfo.Length == 0 && endpoint.Fragment.Length == 0
        ? null : "Enter an absolute HTTP(S) address without credentials or a fragment.";
}

internal enum CollectorProtocol { Grpc = 0, HttpProtobuf = 1 }

internal sealed class CollectorOptions : CollectorSettings
{
    public CollectorProtocol Protocol { get; init; } = CollectorProtocol.HttpProtobuf;
}
