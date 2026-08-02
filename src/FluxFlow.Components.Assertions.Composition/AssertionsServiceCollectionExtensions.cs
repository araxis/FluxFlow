using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Assertions.Composition;

public static class AssertionsServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddAssertions(
        this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(
            AssertionsComponentDefinition.Types.Assertion,
            ConfigureAssertion);
    }

    private static void ConfigureAssertion(ComponentRegistrationBuilder component)
    {
        component.UseFactory(CreateJsonAssertionNode);
        component.WithDisplay(
            displayName: "Assertion",
            category: "Assertions",
            summary: "Evaluates a JSON value and returns a typed assertion result or error message.",
            iconKey: "check-circle",
            preferredNodeName: "assert",
            suggestedEditorWidth: 420);
        component.AddInput<JsonElement>(
            AssertionsComponentDefinition.Ports.Input,
            displayName: "Input",
            group: "Messages",
            order: 0,
            summary: "Immutable value to evaluate.",
            isPrimary: true);
        component.AddOutput<AssertionResult<JsonElement>>(
            AssertionsComponentDefinition.Ports.Output,
            displayName: "Output",
            group: "Results",
            order: 1,
            summary: "Typed assertion outcome or workflow error.",
            isPrimary: true);

        component.AddOption<string>(
            AssertionsComponentDefinition.Options.Expression,
            OptionValueKind.Expression,
            displayName: "Expression",
            helperText: "Boolean expression evaluated for each input message.",
            isRequired: true,
            section: "Assertions",
            importance: OptionDesignMetadataAttributeValues.Primary,
            editor: OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: AssertionsComponentDefinition.Resources.Engine);
        AddTextOption(component, AssertionsComponentDefinition.Options.ExpressionId, "Expression ID", "Optional diagnostic identifier emitted with assertion diagnostics.", "Diagnostics");
        AddTextOption(component, AssertionsComponentDefinition.Options.ExpressionName, "Expression Name", "Optional diagnostic name emitted with assertion diagnostics.", "Diagnostics");
        component.AddOption<string>(
            AssertionsComponentDefinition.Options.InputType,
            OptionValueKind.Text,
            displayName: "Input Type",
            helperText: "Optional semantic type name for the JSON input.",
            defaultValue: AssertionOptions.ObjectTypeName,
            section: "Type Metadata",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<int>(
            AssertionsComponentDefinition.Options.BoundedCapacity,
            OptionValueKind.Number,
            displayName: "Bounded Capacity",
            helperText: "Capacity used for bounded processing and reliable normal-data output.",
            defaultValue: 128,
            min: 1,
            section: "Runtime",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<string>(
            AssertionsComponentDefinition.Options.Description,
            OptionValueKind.Text,
            displayName: "Description",
            helperText: "Description included in assertion results and diagnostics.",
            defaultValue: AssertionOptions.DefaultDescription,
            section: "Results",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(
            AssertionsComponentDefinition.Options.FailureMessage,
            OptionValueKind.Text,
            displayName: "Failure Message",
            helperText: "Message included when the assertion fails.",
            defaultValue: AssertionOptions.DefaultFailureMessage,
            section: "Results",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Text);

        component.AddResource<IFlowExpressionEngine>(
            AssertionsComponentDefinition.Resources.Engine,
            displayName: "Engine",
            order: 0,
            summary: "Keyed expression engine used to evaluate assertion expressions.",
            isRequired: true,
            designValueType: nameof(IFlowExpressionEngine),
            ownership: ResourceDesignMetadataAttributeValues.HostOwned,
            pickerKind: ResourceDesignMetadataAttributeValues.ExpressionEngine,
            keyPattern: "Resources.{name}");
        component.AddResource<IFlowMapContextFactory<JsonElement>>(
            AssertionsComponentDefinition.Resources.ContextFactory,
            displayName: "Context Factory",
            order: 1,
            summary: "Optional keyed input context factory for custom expression variables.",
            designValueType: nameof(IFlowMapContextFactory<JsonElement>),
            ownership: ResourceDesignMetadataAttributeValues.HostOwned,
            pickerKind: ResourceDesignMetadataAttributeValues.ContextFactory,
            keyPattern: "Resources.{name}");
        component.AddResource<TimeProvider>(
            AssertionsComponentDefinition.Resources.Clock,
            displayName: "Clock",
            order: 2,
            summary: "Optional keyed clock for deterministic assertion results and diagnostics.",
            designValueType: nameof(TimeProvider),
            ownership: ResourceDesignMetadataAttributeValues.HostOwned,
            pickerKind: ResourceDesignMetadataAttributeValues.Clock,
            keyPattern: "Resources.{name}");
    }

    private static void AddTextOption(
        ComponentRegistrationBuilder component,
        string name,
        string displayName,
        string helperText,
        string section)
        => component.AddOption<string>(
            name,
            OptionValueKind.Text,
            displayName,
            helperText,
            section: section,
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Text);

    private static ValueTask<ComponentInstance> CreateJsonAssertionNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<AssertionOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            AssertionsComponentDefinition.Resources.Engine);
        var contextFactory = context.GetResource<IFlowMapContextFactory<JsonElement>>(
            AssertionsComponentDefinition.Resources.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            AssertionsComponentDefinition.Resources.Clock);
        var node = new JsonAssertionNode(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    AssertionsComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<AssertionResult<JsonElement>>(
                    AssertionsComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
