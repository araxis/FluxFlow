using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Components.Source;
using FluxFlow.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Sources;

public static class SourceRegistrationExtensions
{
    public static IServiceCollection AddSource(this IServiceCollection services, string? baseDirectory = null)
    {
        var directory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        services.AddKeyedSingleton<ComponentRegistration>("source", (registrations, section) => Configure(registrations, section, directory));
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section, string directory)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadRequiredConfiguration<SourceConfiguration>();
        ArgumentNullException.ThrowIfNull(config.Values);
        if (config.OutputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(config.OutputCapacity));
        var json = config.Values.Read(directory);
        services.AddSourceNode(address, json, config.OutputCapacity);

        services.AddLinks(address, config);
    }
}
