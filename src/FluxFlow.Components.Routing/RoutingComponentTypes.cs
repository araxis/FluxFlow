using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Routing;

public static class RoutingComponentTypes
{
    public static readonly NodeType Switch = new("flow.switch");
    public static readonly NodeType Correlation = new("flow.correlation");
    public static readonly NodeType Window = new("flow.window");
}
