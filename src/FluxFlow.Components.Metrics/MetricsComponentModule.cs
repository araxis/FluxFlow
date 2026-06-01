using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Metrics;

public sealed class MetricsComponentModule : IFlowNodeModule
{
    public MetricsComponentModule()
    {
        Registrations =
        [
            new FlowNodeRegistration(
                MetricsComponentTypes.Aggregate,
                MetricsAggregateNode.Create)
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
