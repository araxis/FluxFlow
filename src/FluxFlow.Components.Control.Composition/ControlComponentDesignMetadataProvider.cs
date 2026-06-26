using FluxFlow.Components.Control.Options;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Control.Composition;

public sealed class ControlComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateFilterMetadata(),
            CreateWhenMetadata()
        ];

    private static ComponentDesignMetadata CreateFilterMetadata()
        => CreateControlMetadata(
            ControlCompositionNodeTypes.Filter,
            "Filter",
            "Forwards input messages only when an expression matches.",
            "filter",
            "filter",
            [
                InputPort(),
                OutputPort(
                    ControlCompositionPortNames.Output,
                    "Output",
                    "Messages",
                    1,
                    "Input message when the expression matches.",
                    isPrimary: true)
            ]);

    private static ComponentDesignMetadata CreateWhenMetadata()
        => CreateControlMetadata(
            ControlCompositionNodeTypes.When,
            "When",
            "Routes input messages to true and false branches.",
            "branch",
            "when",
            [
                InputPort(),
                OutputPort(
                    ControlCompositionPortNames.Output,
                    "Output",
                    "Messages",
                    1,
                    "Primary true-branch output alias.",
                    isPrimary: true,
                    attributes: new Dictionary<string, string>
                    {
                        ["aliasOf"] = ControlCompositionPortNames.WhenTrue
                    }),
                OutputPort(
                    ControlCompositionPortNames.WhenTrue,
                    "When True",
                    "Branches",
                    2,
                    "Input message when the expression matches."),
                OutputPort(
                    ControlCompositionPortNames.WhenFalse,
                    "When False",
                    "Branches",
                    3,
                    "Input message when the expression does not match.")
            ]);

    private static ComponentDesignMetadata CreateControlMetadata(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        IReadOnlyList<PortDesignMetadata> ports) => new()
        {
            Type = new ComponentType(type),
            DisplayName = displayName,
            Category = "Control",
            Summary = summary,
            IconKey = iconKey,
            PreferredNodeName = preferredNodeName,
            SuggestedEditorWidth = 420,
            Options = CreateExpressionOptions(),
            Resources = CreateExpressionResources(),
            Ports = ports
        };

    private static PortDesignMetadata InputPort() => new()
    {
        Name = new ComponentPortName(ControlCompositionPortNames.Input),
        Direction = PortDirection.Input,
        DisplayName = "Input",
        Group = "Messages",
        Order = 0,
        Summary = "Input message.",
        ValueType = "TInput",
        IsPrimary = true
    };

    private static PortDesignMetadata OutputPort(
        string name,
        string displayName,
        string group,
        int order,
        string summary,
        bool isPrimary = false,
        IReadOnlyDictionary<string, string>? attributes = null) => new()
        {
            Name = new ComponentPortName(name),
            Direction = PortDirection.Output,
            DisplayName = displayName,
            Group = group,
            Order = order,
            Summary = summary,
            ValueType = "TInput",
            IsPrimary = isPrimary,
            Attributes = attributes ?? new Dictionary<string, string>()
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

    private static IReadOnlyList<ResourceDesignMetadata> CreateExpressionResources()
        =>
        [
            new ResourceDesignMetadata
            {
                Name = ControlCompositionResourceNames.Engine,
                DisplayName = "Engine",
                Order = 0,
                Summary = "Keyed expression engine used to evaluate control expressions.",
                ValueType = nameof(IFlowExpressionEngine),
                IsRequired = true
            },
            new ResourceDesignMetadata
            {
                Name = ControlCompositionResourceNames.ContextFactory,
                DisplayName = "Context Factory",
                Order = 1,
                Summary = "Optional keyed input context factory for custom expression variables.",
                ValueType = "IFlowMapContextFactory<TInput>"
            },
            new ResourceDesignMetadata
            {
                Name = ControlCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 2,
                Summary = "Optional keyed clock for deterministic diagnostics.",
                ValueType = nameof(TimeProvider)
            }
        ];
}
