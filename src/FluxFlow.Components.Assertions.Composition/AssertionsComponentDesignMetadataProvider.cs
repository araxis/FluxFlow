using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Assertions.Composition;

public sealed class AssertionsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateAssertionMetadata()];

    private static ComponentDesignMetadata CreateAssertionMetadata() => new()
    {
        Type = new ComponentType(AssertionsCompositionNodeTypes.Assert),
        DisplayName = "Assertion",
        Category = "Assertions",
        Summary = "Evaluates an input message and emits assertion results plus optional routed inputs.",
        IconKey = "check-circle",
        PreferredNodeName = "assert",
        SuggestedEditorWidth = 420,
        Options =
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
                HelperText = "Optional diagnostic identifier emitted with assertion diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "expressionName",
                Kind = OptionValueKind.Text,
                DisplayName = "Expression Name",
                HelperText = "Optional diagnostic name emitted with assertion diagnostics."
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
                DefaultValue = AssertionOptions.ObjectTypeName,
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
            },
            new OptionDesignMetadata
            {
                Name = "description",
                Kind = OptionValueKind.Text,
                DisplayName = "Description",
                DefaultValue = AssertionOptions.DefaultDescription,
                HelperText = "Description included in assertion results and diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "failureMessage",
                Kind = OptionValueKind.Text,
                DisplayName = "Failure Message",
                DefaultValue = AssertionOptions.DefaultFailureMessage,
                HelperText = "Message included when the assertion fails."
            },
            new OptionDesignMetadata
            {
                Name = "emitPassedInput",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Emit Passed Input",
                DefaultValue = true,
                HelperText = "Emit matching input messages on the Passed output."
            },
            new OptionDesignMetadata
            {
                Name = "emitFailedInput",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Emit Failed Input",
                DefaultValue = true,
                HelperText = "Emit failing input messages on the Failed output."
            }
        ],
        Ports =
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(AssertionsCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "Input message to evaluate.",
                ValueType = "TInput",
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(AssertionsCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Results",
                Order = 1,
                Summary = "Assertion result.",
                ValueType = nameof(FlowAssertionResult),
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(AssertionsCompositionPortNames.Passed),
                Direction = PortDirection.Output,
                DisplayName = "Passed",
                Group = "Branches",
                Order = 2,
                Summary = "Original input when the assertion passes.",
                ValueType = "TInput"
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(AssertionsCompositionPortNames.Failed),
                Direction = PortDirection.Output,
                DisplayName = "Failed",
                Group = "Branches",
                Order = 3,
                Summary = "Original input when the assertion fails.",
                ValueType = "TInput"
            }
        ]
    };
}
