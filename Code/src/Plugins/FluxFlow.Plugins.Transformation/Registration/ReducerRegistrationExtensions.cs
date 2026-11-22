using FluxFlow.Application;
using FluxFlow.Components.Reducer;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Reducing;

public static class ReducerRegistrationExtensions
{
    public static IServiceCollection AddReducer(this IServiceCollection services, string? baseDirectory = null)
    {
        var directory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        services.AddKeyedSingleton<ComponentRegistration>("reducer", (registrations, section) => Configure(registrations, section, directory));
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section, string directory)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadRequiredConfiguration<ReducerConfiguration>();
        config.Validate();
        var initial = new JsonFlowValue(config.InitialState.Read(directory));
        services.AddReducerNode(address, initial, provider => provider.GetExpressionEngine(config.Engine),
            () => config.Reduce.Read(directory, config.Engine), () => config.AreEqual.Read(directory, config.Engine),
            config.OutputCapacity);
        services.AddLinks(address, config);
    }
}
