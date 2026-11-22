using System.Text.Json;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using FluxFlow.Authoring;

namespace FluxFlow.Studio.UI.Features.Designer.Canvas;

/// <summary>
/// A canvas node that carries the domain identity (component type + node name)
/// alongside the diagram geometry. Each logical port has a handle on both sides.
/// </summary>
public sealed class FlowNodeModel : NodeModel
{
    public FlowNodeModel(string nodeName, IComponentDescription description, Point position,
        IReadOnlyDictionary<string, JsonElement>? configuration = null)
        : base(position)
    {
        Description = description;
        if (configuration is not null)
            foreach (var property in configuration) Configuration.Add(property.Key, property.Value);
        var palette = description.GetPresentation(Configuration);
        NodeName = nodeName;
        ComponentType = palette.ComponentType;
        Title = palette.DisplayName;
        InputPortNames = palette.Inputs.Select(port => port.Name).ToArray();
        SignalInputPortNames = palette.SignalInputs.Select(port => port.Name).ToArray();
        OutputPortNames = palette.Outputs.Select(port => port.Name).ToArray();

        foreach (var port in palette.Inputs.Concat(palette.SignalInputs)) AddHandles(port.Name, isInput: true, colorRole: port.ColorRole);
        foreach (var port in palette.Outputs) AddHandles(port.Name, isInput: false, colorRole: port.ColorRole);
        if (OutputPortNames.Contains("Output")) AddHandles("Error", isInput: false, isError: true);

        void AddHandles(string name, bool isInput, bool isError = false, string? colorRole = null)
        {
            AddPort(new FlowPortModel(this, name, isInput, PortAlignment.Left, isError, colorRole));
            AddPort(new FlowPortModel(this, name, isInput, PortAlignment.Right, isError, colorRole));
        }
    }

    public IComponentDescription Description { get; }
    public PaletteItemModel Presentation => Description.GetPresentation(Configuration);

    public string NodeName { get; internal set; }

    internal long TraceFocusVersion { get; set; }
    internal long ActivityVersion { get; set; }

    public string ComponentType { get; }

    public string? Color { get; set; }
    public string? DefaultColor => Presentation.DefaultColor;
    public string AccentColor => Color ?? DefaultColor ?? "var(--mud-palette-primary)";

    public IReadOnlyList<string> InputPortNames { get; }

    public IReadOnlyList<string> SignalInputPortNames { get; }

    public IReadOnlyList<string> OutputPortNames { get; }

    /// <summary>Per-node option values the inspector edits; persisted as the node configuration.</summary>
    public Dictionary<string, JsonElement> Configuration { get; } = new(StringComparer.Ordinal);
}
