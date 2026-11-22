using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Batch;

public static class BatchWorkflowExtensions
{
    public static WorkflowBuilder Batch(this WorkflowBuilder workflow, string name, int batchSize, TimeSpan maxWait,
        int? outputCapacity = null)
    {
        workflow.Services.AddBatchNode(workflow.Address(name), batchSize, maxWait, outputCapacity ?? 128);
        return workflow;
    }

    public static IServiceCollection AddBatchNode(this IServiceCollection services, ComponentAddress address,
        int batchSize, TimeSpan maxWait, int outputCapacity)
        => services.AddComponentFactory(address, provider => new Batch(new BatchOptions
        {
            BatchSize = batchSize, MaxWait = maxWait, OutputCapacity = outputCapacity,
            TimeProvider = provider.GetService<TimeProvider>() ?? TimeProvider.System
        })).AddInputPort<Batch>(address, "Input", node => node.Input)
           .AddOutputPort<Batch>(address, "Output", node => node.Output)
           .AddEventPort<Batch>(address, node => node.Events);
}
