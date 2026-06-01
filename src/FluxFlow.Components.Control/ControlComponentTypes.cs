using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Control;

public static class ControlComponentTypes
{
    public static readonly NodeType Filter = new("flow.filter");
    public static readonly NodeType When = new("flow.when");
}
