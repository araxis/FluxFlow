using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Mapping;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Deduplicate;

public static class DeduplicateWorkflowExtensions
{
    public static WorkflowBuilder Deduplicate(this WorkflowBuilder workflow, string name, string keySelector,
        TimeSpan retention, int? maxKeys = null, bool? ignoreCase = null, int? outputCapacity = null)
    {
        workflow.Services.AddDeduplicateNode(workflow.Address(name),
            provider => (provider.GetKeyedService<IFlowExpressionEngine>("jsonata") ?? JsonataExpressions.Default)
                .Compile<string>(keySelector), retention, maxKeys ?? 1024, ignoreCase ?? false, outputCapacity ?? 128);
        return workflow;
    }

    public static IServiceCollection AddDeduplicateNode(this IServiceCollection services, ComponentAddress address,
        Func<IServiceProvider, Func<FlowMapContext, string>> compileSelector, TimeSpan retention, int maxKeys,
        bool ignoreCase, int outputCapacity)
        => services.AddComponentFactory(address, provider =>
        {
            var select = compileSelector(provider);
            var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            return new Deduplicate<string>(new DeduplicateOptions<string>
            {
                KeySelector = message => select(FlowMapContext.ForMessage(message)),
                Comparer = comparer,
                Retention = retention,
                MaxKeys = maxKeys,
                TimeProvider = provider.GetService<TimeProvider>() ?? TimeProvider.System,
                OutputCapacity = outputCapacity
            });
        }).AddInputPort<Deduplicate<string>>(address, "Input", node => node.Input)
          .AddOutputPort<Deduplicate<string>>(address, "Output", node => node.Output)
          .AddEventPort<Deduplicate<string>>(address, node => node.Events);
}
