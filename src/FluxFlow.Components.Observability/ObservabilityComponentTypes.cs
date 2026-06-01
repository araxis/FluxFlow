using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Observability;

public static class ObservabilityComponentTypes
{
    public static readonly NodeType Counter = new("flow.counter");
    public static readonly NodeType Logger = new("flow.logger");
    public static readonly NodeType Metrics = new("flow.metrics");
}
