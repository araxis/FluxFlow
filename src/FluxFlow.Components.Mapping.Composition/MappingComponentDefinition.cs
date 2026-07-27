using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Mapping.Composition;

public static partial class MappingComponentDefinition
{
    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        => [CreateMapperMetadata()];

    private static ComponentDesignMetadata CreateMapperMetadata()
        => new ComponentDesignMetadataBuilder(MappingComponentDefinition.Types.Mapper)
            .WithDisplay(
                displayName: "Mapper",
                category: "Mapping",
                summary: "Maps schema-less JSON inputs and carries mapping failures as workflow errors.",
                iconKey: "map",
                preferredNodeName: "map",
                suggestedEditorWidth: 420)
            .AddOption(OptionDesignMetadataFactory.Expression(
                Options.Expression,
                "Expression",
                "Expression evaluated for each input message.",
                "Mapping",
                OptionDesignMetadataAttributeValues.Primary,
                isRequired: true,
                relatedResource: MappingComponentDefinition.Resources.Engine))
            .AddOption(OptionDesignMetadataFactory.Text(
                Options.ExpressionId,
                "Expression ID",
                "Optional diagnostic identifier emitted with mapper diagnostics.",
                "Diagnostics",
                OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(OptionDesignMetadataFactory.Text(
                Options.ExpressionName,
                "Expression Name",
                "Optional diagnostic name emitted with mapper diagnostics.",
                "Diagnostics",
                OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(OptionDesignMetadataFactory.TypeName(
                Options.InputType,
                "Input Type",
                MapperOptions.ObjectTypeName,
                "Optional semantic type name for the JSON input."))
            .AddOption(OptionDesignMetadataFactory.TypeName(
                Options.OutputType,
                "Output Type",
                MapperOptions.ObjectTypeName,
                "Optional semantic type name for the mapped JSON value."))
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(128))
            .AddResource(ResourceDesignMetadataFactory.HostOwned(
                MappingComponentDefinition.Resources.Engine,
                ResourceDesignMetadataAttributeValues.ExpressionEngine,
                "Engine",
                0,
                "Keyed expression engine service used to evaluate mapper expressions.",
                nameof(IFlowExpressionEngine),
                isRequired: true,
                keyPattern: "Resources.{name}"))
            .AddResource(ResourceDesignMetadataFactory.HostOwned(
                MappingComponentDefinition.Resources.ContextFactory,
                ResourceDesignMetadataAttributeValues.ContextFactory,
                "Context Factory",
                1,
                "Optional keyed mapping context factory for custom expression variables.",
                nameof(IMappingContextFactory),
                keyPattern: "Resources.{name}"))
            .AddResource(ResourceDesignMetadataFactory.HostOwned(
                MappingComponentDefinition.Resources.Clock,
                ResourceDesignMetadataAttributeValues.Clock,
                "Clock",
                2,
                "Optional keyed clock for deterministic mapper diagnostics.",
                nameof(TimeProvider),
                keyPattern: "Resources.{name}"))
            .AddInputPort(
                MappingComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Values",
                order: 0,
                summary: "Immutable value to map.",
                valueType: nameof(JsonElement),
                isPrimary: true)
            .AddOutputPort(
                MappingComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "Mapped JSON value; mapping failures use the message error case.",
                valueType: nameof(JsonElement),
                isPrimary: true)
            .Build();


    public static class Options
    {
        public const string Expression = "expression";
        public const string ExpressionId = "expressionId";
        public const string ExpressionName = "expressionName";
        public const string InputType = "inputType";
        public const string OutputType = "outputType";
        public const string BoundedCapacity = "boundedCapacity";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Mapper =>
            [
                ComponentOptions.Metadata<string>(Options.Expression, isRequired: true),
                ComponentOptions.Metadata<string>(Options.ExpressionId),
                ComponentOptions.Metadata<string>(Options.ExpressionName),
                ComponentOptions.Metadata<string>(Options.InputType),
                ComponentOptions.Metadata<string>(Options.OutputType),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Mapper =>
            [
                ComponentResources.Metadata<IFlowExpressionEngine>(Resources.Engine, isRequired: true),
                ComponentResources.Metadata<IMappingContextFactory>(Resources.ContextFactory),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Mapper = "data.map";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Engine = "engine";
    
        public const string ContextFactory = "contextFactory";
    
        public const string Clock = "clock";
    }
}
