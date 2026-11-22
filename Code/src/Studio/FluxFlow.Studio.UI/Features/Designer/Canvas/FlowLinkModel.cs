using Blazor.Diagrams.Core.Models;
using FluxFlow.Authoring;
using PortModel = Blazor.Diagrams.Core.Models.PortModel;

namespace FluxFlow.Studio.UI.Features.Designer.Canvas;

public sealed class FlowLinkModel(PortModel source, PortModel target, ConfigurationLink original) : LinkModel(source, target)
{
    public ConfigurationLink Original { get; } = original;
}
