using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Sources.Composition;

public static class SourcesServiceCollectionExtensions
{
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                GeneratedDescriptor,
                SequenceDescriptor
            ],
            SourcesComponentDefinition.CreateMetadata());

    private const string GeneratedItemsConfigurationName = "items";

    internal static ComponentDescriptor GeneratedDescriptor { get; } = new(
        SourcesComponentDefinition.Types.Generated,
        CreateGeneratedSourceNode,
        outputs:
        [
            ComponentPorts.Metadata<JsonElement>(SourcesComponentDefinition.Ports.Output)
        ],
        options: SourcesComponentDefinition.CreateOptions(SourcesComponentDefinition.Types.Generated),
        resources: SourcesComponentDefinition.CreateResources(SourcesComponentDefinition.Types.Generated));

    internal static ComponentDescriptor SequenceDescriptor { get; } = new(
        SourcesComponentDefinition.Types.Sequence,
        CreateSequenceSourceNode,
        outputs:
        [
            ComponentPorts.Metadata<SequenceItem>(SourcesComponentDefinition.Ports.Output)
        ],
        options: SourcesComponentDefinition.CreateOptions(SourcesComponentDefinition.Types.Sequence),
        resources: SourcesComponentDefinition.CreateResources(SourcesComponentDefinition.Types.Sequence));

    public static IServiceCollection AddSourcesComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateGeneratedSourceNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<GeneratedSourceOptions>();
        var items = DecodeGeneratedItems(context);
        var clock = context.GetResource<TimeProvider>(
            SourcesComponentDefinition.Resources.Clock);
        var node = new GeneratedSourceNode(options, items, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    SourcesComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateSequenceSourceNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SequenceSourceOptions>();
        var clock = context.GetResource<TimeProvider>(
            SourcesComponentDefinition.Resources.Clock);
        var node = new SequenceSourceNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<SequenceItem>(
                    SourcesComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static IReadOnlyList<JsonElement> DecodeGeneratedItems(
        ComponentActivationContext context)
    {
        var configuredItems = context.GetConfigurationValue<JsonElement>(
            GeneratedItemsConfigurationName);
        if (configuredItems.ValueKind == JsonValueKind.Undefined)
        {
            return [];
        }

        return configuredItems.ValueKind == JsonValueKind.Array
            ? configuredItems.EnumerateArray().Select(static item => item.Clone()).ToArray()
            : [configuredItems.Clone()];
    }
}
