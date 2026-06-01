using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Observability;

public sealed class ObservabilityComponentModule : IFlowNodeModule
{
    public ObservabilityComponentModule(ObservabilityComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                ObservabilityComponentTypes.Counter,
                context => ObservabilityNodeFactory.CreateCounter(context, options)),
            new FlowNodeRegistration(
                ObservabilityComponentTypes.Logger,
                context => ObservabilityNodeFactory.CreateLogger(context, options)),
            new FlowNodeRegistration(
                ObservabilityComponentTypes.Metrics,
                context => ObservabilityNodeFactory.CreateMetrics(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
