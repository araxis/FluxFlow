using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Data;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Assertions.Composition;

public sealed class AssertionsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    // Explicit generic registrations retain AssertionsCompositionPortNames.Passed
    // and AssertionsCompositionPortNames.Failed as compatibility-only ports.
    // Fixed canonical metadata intentionally exposes only Input and Output.
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateAssertionMetadata()];

    private static ComponentDesignMetadata CreateAssertionMetadata()
        => new ComponentDesignMetadataBuilder(AssertionsCompositionNodeTypes.Assert)
            .WithDisplay(
                displayName: "Assertion",
                category: "Assertions",
                summary: "Evaluates a FlowValue and returns pass, fail, or expected evaluation-error results.",
                iconKey: "check-circle",
                preferredNodeName: "assert",
                suggestedEditorWidth: 420)
            .AddAttribute(ComponentDesignMetadataAttributeNames.Aliases, AssertionsCompositionNodeTypes.LegacyAssert)
            .AddAttribute("omittedOptions", "emitPassedInput,emitFailedInput")
            .AddAttribute(
                "omittedOptionsReason",
                "Routed-input flags apply only to explicit generic compatibility registrations; the canonical node has one result output.")
            .AddOption(
                "expression",
                OptionValueKind.Expression,
                displayName: "Expression",
                helperText: "Boolean expression evaluated for each input message.",
                isRequired: true,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Assertions",
                    importance: OptionDesignMetadataAttributeValues.Primary,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: AssertionsCompositionResourceNames.Engine))
            .AddOption(
                "expressionId",
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional diagnostic identifier emitted with assertion diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "expressionName",
                OptionValueKind.Text,
                displayName: "Expression Name",
                helperText: "Optional diagnostic name emitted with assertion diagnostics.",
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
                defaultValue: AssertionOptions.ObjectTypeName,
                helperText: "Optional semantic type name for the FlowValue input.",
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
            .AddOption(
                "description",
                OptionValueKind.Text,
                displayName: "Description",
                helperText: "Description included in assertion results and diagnostics.",
                defaultValue: AssertionOptions.DefaultDescription,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Results",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "failureMessage",
                OptionValueKind.Text,
                displayName: "Failure Message",
                helperText: "Message included when the assertion fails.",
                defaultValue: AssertionOptions.DefaultFailureMessage,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Results",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddResource(
                AssertionsCompositionResourceNames.Engine,
                displayName: "Engine",
                order: 0,
                summary: "Keyed expression engine used to evaluate assertion expressions.",
                valueType: nameof(IFlowExpressionEngine),
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ExpressionEngine,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                AssertionsCompositionResourceNames.ContextFactory,
                displayName: "Context Factory",
                order: 1,
                summary: "Optional keyed input context factory for custom expression variables.",
                valueType: "IFlowMapContextFactory<FlowValue>",
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ContextFactory,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                AssertionsCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 2,
                summary: "Optional keyed clock for deterministic assertion results and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"))
            .AddInputPort(
                AssertionsCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Immutable value to evaluate.",
                valueType: nameof(FlowValue),
                isPrimary: true)
            .AddOutputPort(
                AssertionsCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Assertion outcome or expected evaluation error.",
                valueType: "FlowResult<FlowValueAssertionResult>",
                isPrimary: true)
            .Build();
}
