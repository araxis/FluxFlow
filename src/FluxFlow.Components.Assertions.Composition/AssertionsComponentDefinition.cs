using System.Text.Json;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Assertions.Composition;

public static class AssertionsComponentDefinition
{
    public static class Types
    {
        public const string Assertion = "data.assert";
    }

    public static class Options
    {
        public const string Expression = "expression";
        public const string ExpressionId = "expressionId";
        public const string ExpressionName = "expressionName";
        public const string InputType = "inputType";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Description = "description";
        public const string FailureMessage = "failureMessage";
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

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Assertion =>
            [
                ComponentOptions.Metadata<string>(Options.Expression, isRequired: true),
                ComponentOptions.Metadata<string>(Options.ExpressionId),
                ComponentOptions.Metadata<string>(Options.ExpressionName),
                ComponentOptions.Metadata<string>(Options.InputType),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<string>(Options.Description),
                ComponentOptions.Metadata<string>(Options.FailureMessage)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Assertion =>
            [
                ComponentResources.Metadata<IFlowExpressionEngine>(
                    Resources.Engine,
                    isRequired: true),
                ComponentResources.Metadata<IFlowMapContextFactory<JsonElement>>(
                    Resources.ContextFactory),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        => [CreateMetadataItem()];

    private static ComponentDesignMetadata CreateMetadataItem()
        => new ComponentDesignMetadataBuilder(Types.Assertion)
            .WithDisplay(
                displayName: "Assertion",
                category: "Assertions",
                summary: "Evaluates a JSON value and returns a typed assertion result or error message.",
                iconKey: "check-circle",
                preferredNodeName: "assert",
                suggestedEditorWidth: 420)
            .AddOption(
                Options.Expression,
                OptionValueKind.Expression,
                displayName: "Expression",
                helperText: "Boolean expression evaluated for each input message.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Assertions",
                    importance: OptionDesignMetadataAttributeValues.Primary,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: Resources.Engine))
            .AddOption(
                Options.ExpressionId,
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional diagnostic identifier emitted with assertion diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.ExpressionName,
                OptionValueKind.Text,
                displayName: "Expression Name",
                helperText: "Optional diagnostic name emitted with assertion diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.InputType,
                OptionValueKind.Text,
                displayName: "Input Type",
                defaultValue: AssertionOptions.ObjectTypeName,
                helperText: "Optional semantic type name for the JSON input.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Type Metadata",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(128))
            .AddOption(
                Options.Description,
                OptionValueKind.Text,
                displayName: "Description",
                helperText: "Description included in assertion results and diagnostics.",
                defaultValue: AssertionOptions.DefaultDescription,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Results",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.FailureMessage,
                OptionValueKind.Text,
                displayName: "Failure Message",
                helperText: "Message included when the assertion fails.",
                defaultValue: AssertionOptions.DefaultFailureMessage,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Results",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddResource(
                Resources.Engine,
                displayName: "Engine",
                order: 0,
                summary: "Keyed expression engine used to evaluate assertion expressions.",
                valueType: nameof(IFlowExpressionEngine),
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ExpressionEngine,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                Resources.ContextFactory,
                displayName: "Context Factory",
                order: 1,
                summary: "Optional keyed input context factory for custom expression variables.",
                valueType: nameof(IFlowMapContextFactory<JsonElement>),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ContextFactory,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                Resources.Clock,
                displayName: "Clock",
                order: 2,
                summary: "Optional keyed clock for deterministic assertion results and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"))
            .AddInputPort(
                Ports.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Immutable value to evaluate.",
                valueType: nameof(JsonElement),
                isPrimary: true)
            .AddOutputPort(
                Ports.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Typed assertion outcome or workflow error.",
                valueType: nameof(AssertionResult<JsonElement>),
                isPrimary: true)
            .Build();
}
