using Blazor.Diagrams.Core.Models;
using FluxFlow.Composition.Links;

namespace FluxFlow.DesignerApp.Features.Designer.Canvas;

public sealed class FlowLinkModel : LinkModel
{
    public FlowLinkModel(
        PortModel source,
        PortModel target,
        ApplicationLinkDeclarationSide declarationSide,
        string? condition)
        : base(source, target)
    {
        DeclarationSide = declarationSide;
        Condition = condition;
    }

    public ApplicationLinkDeclarationSide DeclarationSide { get; }

    public string? Condition { get; }
}
