using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Mapping;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.ChangeFilter;

public static class ChangeFilterWorkflowExtensions
{
    public static WorkflowBuilder ChangeFilter(this WorkflowBuilder workflow, string name, string keySelector,
        bool? ignoreCase = null, int? outputCapacity = null)
    {
        workflow.Services.AddChangeFilterNode(workflow.Address(name),
            provider => (provider.GetKeyedService<IFlowExpressionEngine>("jsonata") ?? JsonataExpressions.Default)
                .Compile<string>(keySelector), ignoreCase ?? false, outputCapacity ?? 128);
        return workflow;
    }

    public static IServiceCollection AddChangeFilterNode(this IServiceCollection services, ComponentAddress address,
        Func<IServiceProvider, Func<FlowMapContext, string>> compileSelector, bool ignoreCase, int outputCapacity)
        => services.AddComponentFactory(address, provider =>
        {
            var select = compileSelector(provider);
            var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            return new ChangeFilter<string>(new ChangeFilterOptions<string>
            {
                KeySelector = message => select(FlowMapContext.ForMessage(message)),
                AreEqual = comparer.Equals,
                OutputCapacity = outputCapacity
            });
        }).AddInputPort<ChangeFilter<string>>(address, "Input", node => node.Input)
          .AddOutputPort<ChangeFilter<string>>(address, "Output", node => node.Output)
          .AddEventPort<ChangeFilter<string>>(address, node => node.Events);
}
