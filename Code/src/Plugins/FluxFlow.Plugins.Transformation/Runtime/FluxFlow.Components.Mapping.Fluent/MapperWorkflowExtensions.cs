using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Mapping;

public static class MapperWorkflowExtensions
{
    public static WorkflowBuilder JsonataMapper(this WorkflowBuilder workflow, string name, string expression,
        bool? handleErrors = null, int? outputCapacity = null)
    {
        workflow.Services.AddMapperNode(workflow.Address(name), "jsonata", provider =>
            (provider.GetKeyedService<IFlowExpressionEngine>("jsonata") ?? JsonataExpressions.Default)
                .Compile<FlowValue>(expression), handleErrors ?? false, outputCapacity ?? 128);
        return workflow;
    }

    public static IServiceCollection AddMapperNode(this IServiceCollection services, ComponentAddress address,
        string engine, Func<IServiceProvider, Func<FlowMapContext, FlowValue>> compile, bool handleErrors, int outputCapacity)
        => services.AddComponentFactory(address, provider =>
        {
            try
            {
                var evaluate = compile(provider);
                ValueTask<FlowValue> Map(FlowMessage message, CancellationToken cancellation)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var result = evaluate(FlowMapContext.ForMessage(message));
                    cancellation.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(result);
                }
                return new Mapper(new MapperOptions { MapAsync = Map, HandleErrors = handleErrors, OutputCapacity = outputCapacity });
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Mapper '{address}' using engine '{engine}' could not be built: {error.Message}", error);
            }
        }).AddInputPort<Mapper>(address, "Input", node => node.Input)
          .AddOutputPort<Mapper>(address, "Output", node => node.Output)
          .AddEventPort<Mapper>(address, "Events", node => node.Events);
}
