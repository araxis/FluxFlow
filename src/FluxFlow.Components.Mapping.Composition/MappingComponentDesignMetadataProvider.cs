using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Options;
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
                summary: "Maps input messages with a host-provided expression engine.",
                iconKey: "map",
                preferredNodeName: "map",
                suggestedEditorWidth: 420)
            .AddOption(
                "expression",
                OptionValueKind.Expression,
                displayName: "Expression",
                helperText: "Expression evaluated for each input message.",
                isRequired: true)
            .AddOption(
                "expressionId",
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional diagnostic identifier emitted with mapper diagnostics.")
            .AddOption(
                "expressionName",
                OptionValueKind.Text,
                displayName: "Expression Name",
                helperText: "Optional diagnostic name emitted with mapper diagnostics.")
            .AddOption(
                "engine",
                OptionValueKind.Text,
                displayName: "Engine",
                helperText: "Diagnostic engine metadata; composition DI selection uses the engine resource.")
            .AddOption(
                "inputType",
                OptionValueKind.Text,
                displayName: "Input Type",
                defaultValue: MapperOptions.ObjectTypeName,
                helperText: "Diagnostic input type metadata; CLR input type comes from the closed registration.")
            .AddOption(
                "outputType",
                OptionValueKind.Text,
                displayName: "Output Type",
                defaultValue: MapperOptions.ObjectTypeName,
                helperText: "Diagnostic output type metadata; CLR output type comes from the closed registration.")
            .AddOption(
                "targetType",
                OptionValueKind.Text,
                displayName: "Target Type",
                helperText: "Optional output type alias used when outputType is object.")
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: 128,
                min: 1)
            .AddResource(
                MappingCompositionResourceNames.Engine,
                displayName: "Engine",
                order: 0,
                summary: "Keyed expression engine service used to evaluate mapper expressions.",
                valueType: nameof(IFlowExpressionEngine),
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ExpressionEngine))
            .AddResource(
                MappingCompositionResourceNames.ContextFactory,
                displayName: "Context Factory",
                order: 1,
                summary: "Optional keyed mapping context factory for custom expression variables.",
                valueType: nameof(IMappingContextFactory),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ContextFactory))
            .AddResource(
                MappingCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 2,
                summary: "Optional keyed clock for deterministic mapper diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock))
            .AddInputPort(
                MappingCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Input message.",
                valueType: "TInput",
                isPrimary: true)
            .AddOutputPort(
                MappingCompositionPortNames.Output,
                displayName: "Output",
                group: "Messages",
                order: 1,
                summary: "Mapped output message.",
                valueType: "TOutput",
                isPrimary: true)
            .AddOutputPort(
                MappingCompositionPortNames.Failed,
                displayName: "Failed",
                group: "Messages",
                order: 2,
                summary: "Original input message when mapping fails.",
                valueType: "TInput")
            .Build();
}
