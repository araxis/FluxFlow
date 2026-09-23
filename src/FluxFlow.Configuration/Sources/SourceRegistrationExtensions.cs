using System.Text.Json;
using FluxFlow.Components.Source;
using FluxFlow.Core.Application;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Sources;

public static class SourceRegistrationExtensions
{
    public static IServiceCollection AddSource(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ComponentRegistration>("source", Configure);
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadConfiguration<SourceConfiguration>()
            ?? throw new ArgumentException($"Missing Source configuration at '{section.Path}'.");
        ArgumentException.ThrowIfNullOrWhiteSpace(config.ValuesJson);
        if (config.OutputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(config.OutputCapacity));
        using var document = JsonDocument.Parse(config.ValuesJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("ValuesJson must contain a JSON array.");
        var values = document.RootElement.EnumerateArray()
            .Select(value => (FlowValue)new JsonFlowValue(value)).ToArray();

        services.AddComponentFactory(address, _ => new Source(new SourceOptions
            { Values = values, OutputCapacity = config.OutputCapacity }))
            .AddInputPort<Source>(address, "Input", node => node.Input)
            .AddOutputPort<Source>(address, "Output", node => node.Output)
            .AddEventPort<Source>(address, node => node.Events);
    }
}
