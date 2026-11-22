using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Routing;

public static class RoutingWorkflowExtensions
{
    public static WorkflowBuilder Gate(this WorkflowBuilder workflow, string name, bool? initiallyOpen = null, int? outputCapacity = null)
    {
        workflow.Services.AddGateNode(workflow.Address(name), initiallyOpen ?? false, outputCapacity ?? 128);
        return workflow;
    }

    public static IServiceCollection AddGateNode(this IServiceCollection services, ComponentAddress address, bool initiallyOpen, int outputCapacity)
        => services.AddComponentFactory(address, _ => new Gate(initiallyOpen, outputCapacity: outputCapacity))
            .AddInputPort<Gate>(address, "Input", node => node.Input)
            .AddInputPort<Gate>(address, "Open", node => node.Open)
            .AddInputPort<Gate>(address, "Close", node => node.Close)
            .AddOutputPort<Gate>(address, "Output", node => node.Output)
            .AddEventPort<Gate>(address, node => node.Events);

    public static WorkflowBuilder Route(this WorkflowBuilder workflow, string name, IReadOnlyDictionary<string, string> predicates,
        int? outputCapacity = null)
    {
        workflow.Services.AddRouterNode(workflow.Address(name), predicates.Keys.ToArray(), "jsonata",
            provider => provider.GetKeyedService<IFlowExpressionEngine>("jsonata") ?? JsonataExpressions.Default,
            route => predicates[route], outputCapacity ?? 128);
        return workflow;
    }

    public static IServiceCollection AddRouterNode(this IServiceCollection services, ComponentAddress address,
        string[] names, string engine, Func<IServiceProvider, IFlowExpressionEngine> resolveEngine,
        Func<string, string> readExpression, int outputCapacity)
    {
        services.AddComponentFactory(address, provider =>
        {
            var compiler = resolveEngine(provider);
            var predicates = new Predicate<FlowMessage>[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i];
                try
                {
                    var evaluate = compiler.Compile<bool>(readExpression(name))
                        ?? throw new InvalidOperationException($"Expression engine '{engine}' returned no compiled expression.");
                    predicates[i] = message => evaluate(FlowMapContext.ForMessage(message));
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException($"Router '{address}' route '{name}' using engine '{engine}' could not be built: {error.Message}", error);
                }
            }
            return new Router(new RouterOptions { Routes = predicates, OutputCapacity = outputCapacity });
        }).AddInputPort<Router>(address, "Input", node => node.Input)
          .AddOutputPort<Router>(address, "Output", node => node.Output)
          .AddOutputPort<Router>(address, "Unmatched", node => node.Unmatched)
          .AddEventPort<Router>(address, node => node.Events);
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            services.AddOutputPort<Router>(address, names[index], node => node.Outputs[index]);
        }
        return services;
    }
}
