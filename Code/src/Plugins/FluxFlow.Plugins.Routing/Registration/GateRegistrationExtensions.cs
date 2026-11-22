using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Components.Routing;
using FluxFlow.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Routing;

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
        var config = section.ReadRequiredConfiguration<GateConfiguration>();
        config.Validate();
        services.AddGateNode(address, config.InitiallyOpen, config.OutputCapacity);

        services.AddLinks(address, config);
        services.AddInputLinks(address.Port("Open"), config.Open);
        services.AddInputLinks(address.Port("Close"), config.Close);
    }
}
