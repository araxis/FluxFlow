using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Mapping.Composition;

public sealed class MappingComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateMapperMetadata()];

    private static ComponentDesignMetadata CreateMapperMetadata()
        => new ComponentDesignMetadataBuilder(MappingCompositionNodeTypes.Mapper)
            .WithDisplay(
                displayName: "Mapper",
                category: "Mapping",
                summary: "Maps FlowValue inputs and returns normal success or error results.",
                iconKey: "map",
                preferredNodeName: "map",
                suggestedEditorWidth: 420)
            .AddAttribute(ComponentDesignMetadataAttributeNames.Aliases, MappingCompositionNodeTypes.LegacyMapper)
            .AddOption(
                "expression",
                OptionValueKind.Expression,
                displayName: "Expression",
                helperText: "Expression evaluated for each input message.",
                isRequired: true,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Mapping",
                    importance: OptionDesignMetadataAttributeValues.Primary,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: MappingCompositionResourceNames.Engine))
            .AddOption(
                "expressionId",
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional diagnostic identifier emitted with mapper diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "expressionName",
                OptionValueKind.Text,
                displayName: "Expression Name",
                helperText: "Optional diagnostic name emitted with mapper diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "engine",
                OptionValueKind.Text,
                displayName: "Engine",
                helperText: "Diagnostic engine metadata; composition DI selection uses the engine resource.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "inputType",
                OptionValueKind.Text,
                displayName: "Input Type",
                defaultValue: MapperOptions.ObjectTypeName,
                helperText: "Optional semantic type name for the FlowValue input.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Type Metadata",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "outputType",
                OptionValueKind.Text,
                displayName: "Output Type",
                defaultValue: MapperOptions.ObjectTypeName,
                helperText: "Optional semantic type name for the mapped FlowValue.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Type Metadata",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "targetType",
                OptionValueKind.Text,
                displayName: "Target Type",
                helperText: "Compatibility alias used when outputType is object.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Type Metadata",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: 128,
                min: 1,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Runtime",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Number))
            .AddResource(
                MappingCompositionResourceNames.Engine,
                displayName: "Engine",
                order: 0,
                summary: "Keyed expression engine service used to evaluate mapper expressions.",
                valueType: nameof(IFlowExpressionEngine),
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ExpressionEngine,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                MappingCompositionResourceNames.ContextFactory,
                displayName: "Context Factory",
                order: 1,
                summary: "Optional keyed mapping context factory for custom expression variables.",
                valueType: nameof(IMappingContextFactory),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ContextFactory,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                MappingCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 2,
                summary: "Optional keyed clock for deterministic mapper diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"))
            .AddInputPort(
                MappingCompositionPortNames.Input,
                displayName: "Input",
                group: "Values",
                order: 0,
                summary: "Immutable value to map.",
                valueType: nameof(FlowValue),
                isPrimary: true)
            .AddOutputPort(
                MappingCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Mapped FlowValue or expected mapping error.",
                valueType: "FlowResult<FlowValue>",
                isPrimary: true)
            .Build();
}
