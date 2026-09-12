using FluxFlow.Json.Links;
using FluxFlow.Core.Addressing;
using FluxFlow.Core;
using FluxFlow.Composition.Links;
using System.Text.Json;

namespace FluxFlow.Components.Designer.Persistence;

public sealed record DesignerApplicationLink
{
    public DesignerApplicationLink(
        ApplicationAddress source,
        ApplicationAddress target,
        string? condition = null,
        ApplicationLinkDeclarationSide declarationSide = ApplicationLinkDeclarationSide.Output,
        IReadOnlyDictionary<string, JsonElement>? parameters = null,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        bool isFallback = false)
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
        Parameters = new ApplicationLinkDeclarationProjection(source, target, condition, declarationSide, parameters, delivery, isFallback).Parameters;
        Delivery = delivery;
        IsFallback = isFallback;
    }

    public ApplicationAddress Source { get; }

    public ApplicationAddress Target { get; }

    public string? Condition { get; }

    public IReadOnlyDictionary<string, JsonElement> Parameters { get; }

    public ApplicationLinkDeclarationSide DeclarationSide { get; }

    public FlowDeliveryPolicy Delivery { get; }

    public bool IsFallback { get; }

    public static DesignerApplicationLink Create(
        ApplicationAddress source,
        ApplicationAddress target,
        string? condition = null,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        bool isFallback = false)
        => new(
            source,
            target,
            condition,
            source.Kind == ApplicationAddressKind.SystemPort
                ? ApplicationLinkDeclarationSide.Input
                : ApplicationLinkDeclarationSide.Output, delivery: delivery, isFallback: isFallback);
}
