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
            builder =>
            {
                AddInputPort(builder);
                AddOutputPort(
                    builder,
                    ControlCompositionPortNames.Output,
                    "Output",
                    "Messages",
                    1,
                    "Input message when the expression matches.",
                    isPrimary: true);
            });

    private static ComponentDesignMetadata CreateWhenMetadata()
        => CreateControlMetadata(
            ControlCompositionNodeTypes.When,
            "When",
            "Routes input messages to true and false branches.",
            "branch",
            "when",
            builder =>
            {
                AddInputPort(builder);
                AddOutputPort(
                    builder,
                    ControlCompositionPortNames.Output,
                    "Output",
                    "Messages",
                    1,
                    "Primary true-branch output alias.",
                    isPrimary: true,
                    attributes: new Dictionary<string, string>
                    {
                        ["aliasOf"] = ControlCompositionPortNames.WhenTrue
                    });
                AddOutputPort(
                    builder,
                    ControlCompositionPortNames.WhenTrue,
                    "When True",
                    "Branches",
                    2,
                    "Input message when the expression matches.");
                AddOutputPort(
                    builder,
                    ControlCompositionPortNames.WhenFalse,
                    "When False",
                    "Branches",
                    3,
                    "Input message when the expression does not match.");
            });

    private static ComponentDesignMetadata CreateControlMetadata(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        Action<ComponentDesignMetadataBuilder> configurePorts)
    {
        var builder = new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName: displayName,
                category: "Control",
                summary: summary,
                iconKey: iconKey,
                preferredNodeName: preferredNodeName,
                suggestedEditorWidth: 420);

        AddExpressionOptions(builder);
        AddExpressionResources(builder);
        configurePorts(builder);

        return builder.Build();
    }

    private static void AddInputPort(ComponentDesignMetadataBuilder builder)
        => builder.AddInputPort(
            ControlCompositionPortNames.Input,
            displayName: "Input",
            group: "Messages",
            order: 0,
            summary: "Input message.",
            valueType: "TInput",
            isPrimary: true);

    private static void AddOutputPort(
        ComponentDesignMetadataBuilder builder,
        string name,
        string displayName,
        string group,
        int order,
        string summary,
        bool isPrimary = false,
        IReadOnlyDictionary<string, string>? attributes = null)
        => builder.AddOutputPort(
            name,
            displayName: displayName,
            group: group,
            order: order,
            summary: summary,
            valueType: "TInput",
            isPrimary: isPrimary,
            attributes: attributes);

    private static void AddExpressionOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                "expression",
                OptionValueKind.Expression,
                displayName: "Expression",
                helperText: "Boolean expression evaluated for each input message.",
                isRequired: true,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Control",
                    importance: OptionDesignMetadataAttributeValues.Primary,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: ControlCompositionResourceNames.Engine))
            .AddOption(
                "expressionId",
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional diagnostic identifier emitted with control diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "expressionName",
                OptionValueKind.Text,
                displayName: "Expression Name",
                helperText: "Optional diagnostic name emitted with control diagnostics.",
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
                defaultValue: ControlExpressionOptions.ObjectTypeName,
                helperText: "Diagnostic input type metadata; CLR input type comes from the closed registration.",
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
                    editor: OptionDesignMetadataAttributeValues.Number));

    private static void AddExpressionResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                ControlCompositionResourceNames.Engine,
                displayName: "Engine",
                order: 0,
                summary: "Keyed expression engine used to evaluate control expressions.",
                valueType: nameof(IFlowExpressionEngine),
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ExpressionEngine,
                    keyPattern: "expression-engine:{name}"))
            .AddResource(
                ControlCompositionResourceNames.ContextFactory,
                displayName: "Context Factory",
                order: 1,
                summary: "Optional keyed input context factory for custom expression variables.",
                valueType: "IFlowMapContextFactory<TInput>",
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ContextFactory,
                    keyPattern: "context-factory:{name}"))
            .AddResource(
                ControlCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 2,
                summary: "Optional keyed clock for deterministic diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "clock:{name}"));
}
