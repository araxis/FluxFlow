using System.Text.Json;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using FluxFlow.DesignerHost;

namespace FluxFlow.DesignerApp.Features.Designer.Canvas;

/// <summary>
/// A canvas node that carries the domain identity (component type + node name)
/// alongside the diagram geometry. Input ports are placed on the left, output
/// ports on the right; the port names are kept for the persistence slice
/// (mapping to and from a <c>GraphModel</c>).
/// </summary>
public sealed class FlowNodeModel : NodeModel
{
    public FlowNodeModel(string nodeName, PaletteItemModel palette, Point position)
        : base(position)
    {
        NodeName = nodeName;
        ComponentType = palette.ComponentType;
        Title = palette.DisplayName;
        InputPortNames = palette.Inputs.Select(port => port.Name).ToArray();
        OutputPortNames = palette.Outputs.Select(port => port.Name).ToArray();

        // Represent ports for linking. Fall back to one of each so every node is
        // connectable even when the metadata declares no fixed ports.
        var inputs = Math.Max(1, InputPortNames.Count);
        var outputs = Math.Max(1, OutputPortNames.Count);
        for (var i = 0; i < inputs; i++)
        {
            AddPort(PortAlignment.Left);
        }

        for (var i = 0; i < outputs; i++)
        {
            AddPort(PortAlignment.Right);
        }
    }

    public string NodeName { get; }

    public string ComponentType { get; }

    public IReadOnlyList<string> InputPortNames { get; }

    public IReadOnlyList<string> OutputPortNames { get; }

    /// <summary>Per-node option values the inspector edits; persisted as the node configuration.</summary>
    public Dictionary<string, JsonElement> Configuration { get; } = new(StringComparer.Ordinal);
}
