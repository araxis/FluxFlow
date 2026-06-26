using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Assertions.Composition;

public sealed class AssertionsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateAssertionMetadata()];

    private static ComponentDesignMetadata CreateAssertionMetadata()
        => new ComponentDesignMetadataBuilder(AssertionsCompositionNodeTypes.Assert)
            .WithDisplay(
                displayName: "Assertion",
                category: "Assertions",
                summary: "Evaluates an input message and emits assertion results plus optional routed inputs.",
                iconKey: "check-circle",
                preferredNodeName: "assert",
                suggestedEditorWidth: 420)
            .AddOption(
                "expression",
                OptionValueKind.Expression,
                displayName: "Expression",
                helperText: "Boolean expression evaluated for each input message.",
                isRequired: true)
            .AddOption(
                "expressionId",
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional diagnostic identifier emitted with assertion diagnostics.")
            .AddOption(
                "expressionName",
                OptionValueKind.Text,
                displayName: "Expression Name",
                helperText: "Optional diagnostic name emitted with assertion diagnostics.")
            .AddOption(
                "engine",
                OptionValueKind.Text,
                displayName: "Engine",
                helperText: "Diagnostic engine metadata; composition DI selection uses the engine resource.")
            .AddOption(
                "inputType",
                OptionValueKind.Text,
                displayName: "Input Type",
                defaultValue: AssertionOptions.ObjectTypeName,
                helperText: "Diagnostic input type metadata; CLR input type comes from the closed registration.")
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: 128,
                min: 1)
            .AddOption(
                "description",
                OptionValueKind.Text,
                displayName: "Description",
                helperText: "Description included in assertion results and diagnostics.",
                defaultValue: AssertionOptions.DefaultDescription)
            .AddOption(
                "failureMessage",
                OptionValueKind.Text,
                displayName: "Failure Message",
                helperText: "Message included when the assertion fails.",
                defaultValue: AssertionOptions.DefaultFailureMessage)
            .AddOption(
                "emitPassedInput",
                OptionValueKind.Boolean,
                displayName: "Emit Passed Input",
                helperText: "Emit matching input messages on the Passed output.",
                defaultValue: true)
            .AddOption(
                "emitFailedInput",
                OptionValueKind.Boolean,
                displayName: "Emit Failed Input",
                helperText: "Emit failing input messages on the Failed output.",
                defaultValue: true)
            .AddResource(
                AssertionsCompositionResourceNames.Engine,
                displayName: "Engine",
                order: 0,
                summary: "Keyed expression engine used to evaluate assertion expressions.",
                valueType: nameof(IFlowExpressionEngine),
                isRequired: true)
            .AddResource(
                AssertionsCompositionResourceNames.ContextFactory,
                displayName: "Context Factory",
                order: 1,
                summary: "Optional keyed input context factory for custom expression variables.",
                valueType: "IFlowMapContextFactory<TInput>")
            .AddResource(
                AssertionsCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 2,
                summary: "Optional keyed clock for deterministic assertion results and diagnostics.",
                valueType: nameof(TimeProvider))
            .AddInputPort(
                AssertionsCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Input message to evaluate.",
                valueType: "TInput",
                isPrimary: true)
            .AddOutputPort(
                AssertionsCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Assertion result.",
                valueType: nameof(FlowAssertionResult),
                isPrimary: true)
            .AddOutputPort(
                AssertionsCompositionPortNames.Passed,
                displayName: "Passed",
                group: "Branches",
                order: 2,
                summary: "Original input when the assertion passes.",
                valueType: "TInput")
            .AddOutputPort(
                AssertionsCompositionPortNames.Failed,
                displayName: "Failed",
                group: "Branches",
                order: 3,
                summary: "Original input when the assertion fails.",
                valueType: "TInput")
            .Build();
}
