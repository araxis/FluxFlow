using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;

namespace FluxFlow.Components.Designer.Persistence;

public sealed record DesignerApplicationLink
{
    public DesignerApplicationLink(
        ApplicationAddress source,
        ApplicationAddress target,
        string? condition = null,
        ApplicationLinkDeclarationSide declarationSide = ApplicationLinkDeclarationSide.Output)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        if (source.Kind is not (ApplicationAddressKind.WorkflowPort or ApplicationAddressKind.SystemPort))
            throw new ArgumentException("Link sources must be workflow or system output ports.", nameof(source));
        if (target.Kind != ApplicationAddressKind.WorkflowPort)
            throw new ArgumentException("Link targets must be workflow input ports.", nameof(target));
        if (condition is not null && string.IsNullOrWhiteSpace(condition))
            throw new ArgumentException("Link conditions cannot be empty.", nameof(condition));
        if (source.Kind == ApplicationAddressKind.SystemPort &&
            declarationSide == ApplicationLinkDeclarationSide.Output)
        {
            throw new ArgumentException(
                "System output links must be declared on their target input.",
                nameof(declarationSide));
        }

        Condition = condition;
        DeclarationSide = declarationSide;
    }

    public ApplicationAddress Source { get; }

    public ApplicationAddress Target { get; }

    public string? Condition { get; }

    public ApplicationLinkDeclarationSide DeclarationSide { get; }

    public static DesignerApplicationLink Create(
        ApplicationAddress source,
        ApplicationAddress target,
        string? condition = null)
        => new(
            source,
            target,
            condition,
            source.Kind == ApplicationAddressKind.SystemPort
                ? ApplicationLinkDeclarationSide.Input
                : ApplicationLinkDeclarationSide.Output);
}
