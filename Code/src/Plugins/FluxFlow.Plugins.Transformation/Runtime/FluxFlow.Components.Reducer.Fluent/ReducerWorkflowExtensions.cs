using System.Text.Json;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Reducer;

public static class ReducerWorkflowExtensions
{
    public static WorkflowBuilder Reduce(this WorkflowBuilder workflow, string name, byte[] initialState,
        string reduce, string areEqual, int? outputCapacity = null)
    {
        using var stream = new MemoryStream(initialState, writable: false);
        using var document = JsonDocument.Parse(stream);
        workflow.Services.AddReducerNode(workflow.Address(name), new JsonFlowValue(document.RootElement),
            provider => provider.GetKeyedService<IFlowExpressionEngine>("jsonata") ?? JsonataExpressions.Default,
            () => reduce, () => areEqual, outputCapacity ?? 128);
        return workflow;
    }

    public static IServiceCollection AddReducerNode(this IServiceCollection services, ComponentAddress address,
        FlowValue initialState, Func<IServiceProvider, IFlowExpressionEngine> resolveEngine,
        Func<string> readReduce, Func<string> readEquality, int outputCapacity)
        => services.AddComponentFactory(address, provider =>
        {
            var engine = resolveEngine(provider);
            return new Reducer(new ReducerOptions
            {
                InitialState = initialState,
                Reduce = ReducerExpressions.CompileReduce(engine, readReduce()),
                AreEqual = ReducerExpressions.CompileEquality(engine, readEquality()),
                OutputCapacity = outputCapacity
            });
        }).AddInputPort<Reducer>(address, "Input", node => node.Input)
          .AddOutputPort<Reducer>(address, "Output", node => node.Output)
          .AddEventPort<Reducer>(address, node => node.Events);
}
