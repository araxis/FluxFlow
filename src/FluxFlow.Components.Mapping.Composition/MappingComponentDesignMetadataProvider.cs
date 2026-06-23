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

    private static ComponentDesignMetadata CreateMapperMetadata() => new()
    {
        Type = new ComponentType(MappingCompositionNodeTypes.Mapper),
        DisplayName = "Mapper",
        Category = "Mapping",
        Summary = "Maps input messages with a host-provided expression engine.",
        IconKey = "map",
        PreferredNodeName = "map",
        SuggestedEditorWidth = 420,
        Options =
        [
            new OptionDesignMetadata
            {
                Name = "expression",
                Kind = OptionValueKind.Expression,
                DisplayName = "Expression",
                HelperText = "Expression evaluated for each input message.",
                IsRequired = true
            },
            new OptionDesignMetadata
            {
                Name = "expressionId",
                Kind = OptionValueKind.Text,
                DisplayName = "Expression ID",
                HelperText = "Optional diagnostic identifier emitted with mapper diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "expressionName",
                Kind = OptionValueKind.Text,
                DisplayName = "Expression Name",
                HelperText = "Optional diagnostic name emitted with mapper diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "engine",
                Kind = OptionValueKind.Text,
                DisplayName = "Engine",
                HelperText = "Diagnostic engine metadata; composition DI selection uses the engine resource."
            },
            new OptionDesignMetadata
            {
                Name = "inputType",
                Kind = OptionValueKind.Text,
                DisplayName = "Input Type",
                DefaultValue = MapperOptions.ObjectTypeName,
                HelperText = "Diagnostic input type metadata; CLR input type comes from the closed registration."
            },
            new OptionDesignMetadata
            {
                Name = "outputType",
                Kind = OptionValueKind.Text,
                DisplayName = "Output Type",
                DefaultValue = MapperOptions.ObjectTypeName,
                HelperText = "Diagnostic output type metadata; CLR output type comes from the closed registration."
            },
            new OptionDesignMetadata
            {
                Name = "targetType",
                Kind = OptionValueKind.Text,
                DisplayName = "Target Type",
                HelperText = "Optional output type alias used when outputType is object."
            },
            new OptionDesignMetadata
            {
                Name = "boundedCapacity",
                Kind = OptionValueKind.Number,
                DisplayName = "Bounded Capacity",
                DefaultValue = 128,
                Min = 1,
                HelperText = "Maximum queued input messages."
            }
        ],
        Resources =
        [
            new ResourceDesignMetadata
            {
                Name = MappingCompositionResourceNames.Engine,
                DisplayName = "Engine",
                Order = 0,
                Summary = "Keyed expression engine service used to evaluate mapper expressions.",
                ValueType = nameof(IFlowExpressionEngine),
                IsRequired = true
            },
            new ResourceDesignMetadata
            {
                Name = MappingCompositionResourceNames.ContextFactory,
                DisplayName = "Context Factory",
                Order = 1,
                Summary = "Optional keyed mapping context factory for custom expression variables.",
                ValueType = nameof(IMappingContextFactory)
            },
            new ResourceDesignMetadata
            {
                Name = MappingCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 2,
                Summary = "Optional keyed clock for deterministic mapper diagnostics.",
                ValueType = nameof(TimeProvider)
            }
        ],
        Ports =
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(MappingCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "Input message.",
                ValueType = "TInput",
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(MappingCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Messages",
                Order = 1,
                Summary = "Mapped output message.",
                ValueType = "TOutput",
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(MappingCompositionPortNames.Failed),
                Direction = PortDirection.Output,
                DisplayName = "Failed",
                Group = "Messages",
                Order = 2,
                Summary = "Original input message when mapping fails.",
                ValueType = "TInput"
            }
        ]
    };
}
