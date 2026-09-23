using FluxFlow.Components.Routing;
using FluxFlow.Core.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Routing;

public static class GateRegistrationExtensions
{
    public static IServiceCollection AddGate(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ComponentRegistration>("gate", Configure);
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadConfiguration<GateConfiguration>()
            ?? throw new ArgumentException($"Missing Gate configuration at '{section.Path}'.");
        if (config.OutputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(config.OutputCapacity));

        services.AddComponentFactory(address, _ => new Gate(config.InitiallyOpen, outputCapacity: config.OutputCapacity))
            .AddInputPort<Gate>(address, "Input", node => node.Input)
            .AddInputPort<Gate>(address, "Open", node => node.Open, signal: true)
            .AddInputPort<Gate>(address, "Close", node => node.Close, signal: true)
            .AddOutputPort<Gate>(address, "Output", node => node.Output)
            .AddEventPort<Gate>(address, node => node.Events);
    }
}
