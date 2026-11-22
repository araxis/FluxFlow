using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Components.Routing;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Application;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Routing;

public static class RouterRegistrationExtensions
{
    public static IServiceCollection AddRouter(this IServiceCollection services, string? expressionBaseDirectory = null)
    {
        var directory = Path.GetFullPath(expressionBaseDirectory ?? AppContext.BaseDirectory);
        services.AddKeyedSingleton<ComponentRegistration>("router", (registrations, section) => Configure(registrations, section, directory));
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section, string directory)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadRequiredConfiguration<RouterConfiguration>();
        config.Validate(address);
        var routes = config.Routes.Keys.ToArray();
        var engine = config.Engine!;
        services.AddRouterNode(address, routes, engine, provider => provider.GetExpressionEngine(engine),
            name => config.Routes[name].Read(directory, engine), config.OutputCapacity);
        foreach (var name in routes)
        {
            var links = section.GetSection(name);
            if (links.Exists())
                services.AddOutputLinks(address.Port(name), OneOrManyPortReferences.Read(links, address));
        }

        services.AddLinks(address, config);
        services.AddOutputLinks(address.Port("Unmatched"), config.Unmatched);
    }
}
