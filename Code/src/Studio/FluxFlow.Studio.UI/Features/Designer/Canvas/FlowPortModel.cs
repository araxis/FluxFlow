using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;

namespace FluxFlow.Studio.UI.Features.Designer.Canvas;

/// <summary>A connection handle whose logical role is independent of its visual side.</summary>
public sealed class FlowPortModel(FlowNodeModel parent, string name, bool isInput,
    PortAlignment alignment, bool isError = false, string? colorRole = null) : PortModel(parent, alignment)
{
    public string Name { get; } = name;
    public bool IsInput { get; } = isInput;
    public bool IsError { get; } = isError;
    public string PortName => IsError ? "Output" : Name;
    public string ColorRole { get; } = isError ? "error" : colorRole ?? (isInput ? "input" : "output");
    private FlowNodeModel Node => (FlowNodeModel)Parent;
    public string? InactiveReason => (IsInput ? Node.Presentation.Inputs.Concat(Node.Presentation.SignalInputs) : Node.Presentation.Outputs)
        .FirstOrDefault(port => port.Name == PortName)?.InactiveReason;
    internal bool HasPreservedLinks { get; set; }
    public bool IsVisible => InactiveReason is null || Node.Ports.OfType<FlowPortModel>()
        .Any(port => port.Name == Name && port.IsInput == IsInput && port.IsError == IsError && (port.Links.Count != 0 || port.HasPreservedLinks));

    public override bool CanAttachTo(ILinkable other) =>
        InactiveReason is null && other is FlowPortModel port && port.InactiveReason is null && port.IsInput != IsInput && base.CanAttachTo(other);
}
