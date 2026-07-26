using System.Text.Json;
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
                summary: "Maps schema-less JSON inputs and carries mapping failures as workflow errors.",
                iconKey: "map",
                preferredNodeName: "map",
                suggestedEditorWidth: 420)
            .AddAttribute(ComponentDesignMetadataAttributeNames.Aliases, string.Join(',', MappingCompositionNodeTypes.MapperDescriptor.Aliases))
            .AddOption(OptionDesignMetadataFactory.Expression(
                "expression",
                "Expression",
                "Expression evaluated for each input message.",
                "Mapping",
                OptionDesignMetadataAttributeValues.Primary,
                isRequired: true,
                relatedResource: MappingCompositionResourceNames.Engine))
            .AddOption(OptionDesignMetadataFactory.Text(
                "expressionId",
                "Expression ID",
                "Optional diagnostic identifier emitted with mapper diagnostics.",
                "Diagnostics",
                OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(OptionDesignMetadataFactory.Text(
                "expressionName",
                "Expression Name",
                "Optional diagnostic name emitted with mapper diagnostics.",
                "Diagnostics",
                OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(OptionDesignMetadataFactory.TypeName(
                "inputType",
                "Input Type",
                MapperOptions.ObjectTypeName,
                "Optional semantic type name for the JSON input."))
            .AddOption(OptionDesignMetadataFactory.TypeName(
                "outputType",
                "Output Type",
                MapperOptions.ObjectTypeName,
                "Optional semantic type name for the mapped JSON value."))
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(128))
            .AddResource(ResourceDesignMetadataFactory.HostOwned(
                MappingCompositionResourceNames.Engine,
                ResourceDesignMetadataAttributeValues.ExpressionEngine,
                "Engine",
                0,
                "Keyed expression engine service used to evaluate mapper expressions.",
                nameof(IFlowExpressionEngine),
                isRequired: true,
                keyPattern: "Resources.{name}"))
            .AddResource(ResourceDesignMetadataFactory.HostOwned(
                MappingCompositionResourceNames.ContextFactory,
                ResourceDesignMetadataAttributeValues.ContextFactory,
                "Context Factory",
                1,
                "Optional keyed mapping context factory for custom expression variables.",
                nameof(IMappingContextFactory),
                keyPattern: "Resources.{name}"))
            .AddResource(ResourceDesignMetadataFactory.HostOwned(
                MappingCompositionResourceNames.Clock,
                ResourceDesignMetadataAttributeValues.Clock,
                "Clock",
                2,
                "Optional keyed clock for deterministic mapper diagnostics.",
                nameof(TimeProvider),
                keyPattern: "Resources.{name}"))
            .AddInputPort(
                MappingCompositionPortNames.Input,
                displayName: "Input",
                group: "Values",
                order: 0,
                summary: "Immutable value to map.",
                valueType: nameof(JsonElement),
                isPrimary: true)
            .AddOutputPort(
                MappingCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Mapped JSON value; mapping failures use the message error case.",
                valueType: nameof(JsonElement),
                isPrimary: true)
            .Build();
}
