using System.Text.Json;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using FluxFlow.DesignerHost;

namespace FluxFlow.DesignerApp.Features.Designer.Canvas;

/// <summary>
/// A canvas node that carries the domain identity (component type + node name)
/// alongside the diagram geometry. Input ports are placed on the left, output
/// ports on the right; the port names are kept for the persistence slice
/// (mapping to and from a canonical Designer workflow model).
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
        SignalInputPortNames = palette.SignalInputs.Select(port => port.Name).ToArray();
        OutputPortNames = palette.Outputs.Select(port => port.Name).ToArray();

        // Keep payload-independent signals separate from typed message inputs.
        // The fallback applies only when the component declares no input shape.
        var inputs = InputPortNames.Count == 0 && SignalInputPortNames.Count == 0
            ? 1
            : InputPortNames.Count;
        var outputs = Math.Max(1, OutputPortNames.Count);
        for (var i = 0; i < inputs; i++)
        {
            AddPort(PortAlignment.Left);
        }

        for (var i = 0; i < SignalInputPortNames.Count; i++)
        {
            AddPort(PortAlignment.Top);
        }

        for (var i = 0; i < outputs; i++)
        {
            AddPort(PortAlignment.Right);
        }
    }

    public string NodeName { get; }

    public string ComponentType { get; }

    public IReadOnlyList<string> InputPortNames { get; }

    public IReadOnlyList<string> SignalInputPortNames { get; }

    public IReadOnlyList<string> OutputPortNames { get; }

    /// <summary>Per-node option values the inspector edits; persisted as the node configuration.</summary>
    public Dictionary<string, JsonElement> Configuration { get; } = new(StringComparer.Ordinal);
}
