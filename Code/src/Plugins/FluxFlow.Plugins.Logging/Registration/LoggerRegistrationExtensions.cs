using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FluxFlow.Components.Logging;

namespace FluxFlow.Configuration.Components.Logging;

public static class LoggerRegistrationExtensions
{
    public static IServiceCollection AddLogger(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddKeyedSingleton<ResourceRegistration>("logging.otlp", (registrations, address, section) =>
        {
            var config = section.Get<OtlpLoggerConfiguration>() ?? throw new ArgumentException($"Missing logging settings at '{section.Path}'.");
            registrations.AddOtlpLogger(address.Value, config.Endpoint, config.Protocol, config.Headers,
                config.ServiceName, config.ExportTimeout, config.ReportExportErrors, config.MinimumLogLevel);
        });
        services.AddKeyedSingleton<ComponentRegistration>("logger", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<LoggerConfiguration>();
            config.Validate();
            registrations.AddLoggerNode(address, config.Category, config.OutputCapacity, config.Logger);
            registrations.AddLinks(address, config);
        });
        return services;
    }
}
