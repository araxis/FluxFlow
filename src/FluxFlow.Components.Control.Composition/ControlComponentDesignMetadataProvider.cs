using FluxFlow.Components.Control.Options;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Control.Composition;

public sealed class ControlComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateFilterMetadata(),
            CreateWhenMetadata()
        ];

    private static ComponentDesignMetadata CreateFilterMetadata() => new()
    {
        Type = new ComponentType(ControlCompositionNodeTypes.Filter),
        DisplayName = "Filter",
        Category = "Control",
        Summary = "Forwards input messages only when an expression matches.",
        IconKey = "filter",
        PreferredNodeName = "filter",
        SuggestedEditorWidth = 420,
        Options = CreateExpressionOptions(),
        Ports =
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ControlCompositionPortNames.Input),
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
                Name = new ComponentPortName(ControlCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Messages",
                Order = 1,
                Summary = "Input message when the expression matches.",
                ValueType = "TInput",
                IsPrimary = true
            }
        ]
    };

    private static ComponentDesignMetadata CreateWhenMetadata() => new()
    {
        Type = new ComponentType(ControlCompositionNodeTypes.When),
        DisplayName = "When",
        Category = "Control",
        Summary = "Routes input messages to true and false branches.",
        IconKey = "branch",
        PreferredNodeName = "when",
        SuggestedEditorWidth = 420,
        Options = CreateExpressionOptions(),
        Ports =
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ControlCompositionPortNames.Input),
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
                Name = new ComponentPortName(ControlCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Messages",
                Order = 1,
                Summary = "Primary true-branch output alias.",
                ValueType = "TInput",
                IsPrimary = true,
                Attributes = new Dictionary<string, string>
                {
                    ["aliasOf"] = ControlCompositionPortNames.WhenTrue
                }
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ControlCompositionPortNames.WhenTrue),
                Direction = PortDirection.Output,
                DisplayName = "When True",
                Group = "Branches",
                Order = 2,
                Summary = "Input message when the expression matches.",
                ValueType = "TInput"
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ControlCompositionPortNames.WhenFalse),
                Direction = PortDirection.Output,
                DisplayName = "When False",
                Group = "Branches",
                Order = 3,
                Summary = "Input message when the expression does not match.",
                ValueType = "TInput"
            }
        ]
    };

    private static IReadOnlyList<OptionDesignMetadata> CreateExpressionOptions()
        =>
        [
            new OptionDesignMetadata
            {
                Name = "expression",
                Kind = OptionValueKind.Expression,
                DisplayName = "Expression",
                HelperText = "Boolean expression evaluated for each input message.",
                IsRequired = true
            },
            new OptionDesignMetadata
            {
                Name = "expressionId",
                Kind = OptionValueKind.Text,
                DisplayName = "Expression ID",
                HelperText = "Optional diagnostic identifier emitted with control diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "expressionName",
                Kind = OptionValueKind.Text,
                DisplayName = "Expression Name",
                HelperText = "Optional diagnostic name emitted with control diagnostics."
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
                DefaultValue = ControlExpressionOptions.ObjectTypeName,
                HelperText = "Diagnostic input type metadata; CLR input type comes from the closed registration."
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
        ];
}
