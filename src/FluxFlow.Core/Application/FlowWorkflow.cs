using FluxFlow.Core.Application;
using FluxFlow.Core.Nodes;
using FluxFlow.Core.Execution;

namespace FluxFlow.Core.Application;

/// <summary>Contains components and their routing declarations. Messages drive processing; workflows have no start/stop lifecycle.</summary>
public sealed class FlowWorkflow
{
    internal Dictionary<string, IFlowNode> Components { get; } = new(StringComparer.Ordinal);
    internal List<ComponentActivityBridge> EventBridges { get; } = [];
    internal List<FlowLink> Links { get; } = [];

    internal FlowWorkflow(string name) => Name = name;
    public string Name { get; }
}
