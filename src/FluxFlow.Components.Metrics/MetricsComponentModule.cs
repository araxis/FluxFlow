using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Metrics;

public sealed class MetricsComponentModule : IFlowNodeModule
{
    public MetricsComponentModule()
        : this(new MetricsComponentOptions())
    {
    }

    public MetricsComponentModule(MetricsComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                MetricsComponentTypes.Aggregate,
                context => MetricsAggregateNode.Create(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
