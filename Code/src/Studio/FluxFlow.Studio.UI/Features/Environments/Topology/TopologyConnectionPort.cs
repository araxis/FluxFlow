using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using FluxFlow.Environments;

namespace FluxFlow.Studio.UI.Features.Environments.Topology;

public sealed class TopologyConnectionPort(TopologyNodeModel node, string path, string type, string label)
    : PortModel(node, node.Definition.Kind == EnvironmentNodeKind.Application ? PortAlignment.Right : PortAlignment.Left)
{
    public string Path { get; } = path;
    public string Type { get; } = type;
    public string Label { get; } = label;
    public string? InitialTargetId { get; set; }
    public bool IsApplication => ((TopologyNodeModel)Parent).Definition.Kind == EnvironmentNodeKind.Application;
    public override bool CanAttachTo(ILinkable other) => other is TopologyConnectionPort target &&
        IsApplication != target.IsApplication && Type == target.Type && base.CanAttachTo(other);
}
