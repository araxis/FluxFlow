using FluxFlow.Components.Routing;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Core.Application;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Routing;

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
        var config = section.ReadConfiguration<RouterConfiguration>()
            ?? throw new ArgumentException($"Missing Router configuration at '{section.Path}'.");
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Engine);
        if (config.OutputCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(config.OutputCapacity));
        var routes = config.Routes.ToArray();
        if (routes.Length == 0) throw new ArgumentException($"Router '{address}' requires at least one route.");
        foreach (var (name, expression) in routes)
        {
            if (new[] { "Input", "Output", "Unmatched", "Events", "Type", "Engine", "Routes", "OutputCapacity" }
                .Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Router '{address}' route '{name}' conflicts with a component property or port.");
            ArgumentNullException.ThrowIfNull(expression);
        }
        var engine = config.Engine.Trim().ToLowerInvariant();
        services.AddComponentFactory(address, provider =>
        {
            var compiler = provider.GetRequiredKeyedService<IFlowExpressionEngine>(engine);
            var predicates = new Predicate<FlowMessage>[routes.Length];
            for (var i = 0; i < routes.Length; i++)
            {
                var (name, expression) = routes[i];
                try
                {
                    var evaluate = compiler.Compile<bool>(expression.Read(directory));
                    predicates[i] = message => evaluate(FlowMapContext.ForMessage(message));
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException($"Router '{address}' route '{name}' using engine '{engine}' could not be built: {error.Message}", error);
                }
            }
            return new Router(new RouterOptions { Routes = predicates, OutputCapacity = config.OutputCapacity });
        }).AddInputPort<Router>(address, "Input", node => node.Input)
          .AddOutputPort<Router>(address, "Output", node => node.Output)
          .AddOutputPort<Router>(address, "Unmatched", node => node.Unmatched)
          .AddEventPort<Router>(address, node => node.Events);
        for (var i = 0; i < routes.Length; i++)
        {
            var index = i;
            services.AddOutputPort<Router>(address, routes[i].Key, node => node.Outputs[index]);
        }
    }
}
