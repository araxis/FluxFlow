using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Routing.Options;

namespace FluxFlow.Components.Routing.Composition;

public sealed class RoutingComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateWindowMetadata(),
            CreateCorrelationMetadata(),
            CreateJoinMetadata()
        ];

    private static ComponentDesignMetadata RoutingMetadata(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        int suggestedEditorWidth,
        IReadOnlyList<OptionDesignMetadata> options,
        Action<ComponentDesignMetadataBuilder> configurePorts,
        IReadOnlyList<ResourceDesignMetadata> resources,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        var builder = new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName: displayName,
                category: "Routing",
                summary: summary,
                iconKey: iconKey,
                preferredNodeName: preferredNodeName,
                suggestedEditorWidth: suggestedEditorWidth);

        foreach (var option in options)
        {
            builder.AddOption(option);
        }

        foreach (var resource in resources)
        {
            builder.AddResource(resource);
        }

        configurePorts(builder);

        if (attributes is not null)
        {
            foreach (var attribute in attributes)
            {
                builder.AddAttribute(attribute.Key, attribute.Value);
            }
        }

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateWindowMetadata()
        => RoutingMetadata(
            RoutingCompositionNodeTypes.Window,
            "Window",
            "Buffers input messages into count- or time-based windows.",
            "panel-top",
            "window",
            420,
        [
            InputTypeOption("inputType"),
            NumberOption(
                "maxItems",
                "Max Items",
                0,
                0,
                "Maximum buffered item count; set timeMilliseconds when zero.",
                "Windowing",
                OptionDesignMetadataAttributeValues.Primary),
            NumberOption(
                "timeMilliseconds",
                "Time Milliseconds",
                0,
                0,
                "Maximum window duration in milliseconds; set maxItems when zero.",
                "Windowing",
                OptionDesignMetadataAttributeValues.Primary),
            BoolOption(
                "emitPartialOnCompletion",
                "Emit Partial On Completion",
                true,
                "Emit a partial window when input completes.",
                "Windowing"),
            BoundedCapacityOption()
        ],
        builder =>
        {
            AddInputPort(builder, RoutingCompositionPortNames.Input, "Input", "Schema-less JSON value.", nameof(JsonElement), 0, isPrimary: true);
            AddOutputPort(builder, RoutingCompositionPortNames.Output, "Output", "Count-, time-, or completion-based window; failures use the message error case.", "FlowWindow<JsonElement>", 1, isPrimary: true);
        },
        [ClockResource(0)]);

    private static ComponentDesignMetadata CreateCorrelationMetadata()
        => RoutingMetadata(
            RoutingCompositionNodeTypes.Correlation,
            "Correlation",
            "Pairs related request and response messages by host-provided key and side selectors.",
            "link",
            "correlate",
            460,
        [
            EngineOption(),
            ExpressionOption(
                "keyExpression",
                "Key Expression",
                "Diagnostic key expression metadata; key selection uses the keySelector resource.",
                RoutingCompositionResourceNames.KeySelector),
            ExpressionOption(
                "sideExpression",
                "Side Expression",
                "Diagnostic side expression metadata; side selection uses the sideSelector resource.",
                RoutingCompositionResourceNames.SideSelector),
            ExpressionIdOption(),
            ExpressionNameOption(),
            InputTypeOption("inputType"),
            TextOption("requestSide", "Request Side", "request", "Side label treated as the request side."),
            TextOption("responseSide", "Response Side", "response", "Side label treated as the response side."),
            BoolOption("caseSensitive", "Case Sensitive", true, "Match keys and sides using case-sensitive comparisons."),
            NumberOption("timeoutMilliseconds", "Timeout Milliseconds", 30_000, 1, "Timeout for pending correlations."),
            NumberOption("maxPending", "Max Pending", 1_024, 1, "Maximum pending correlation keys."),
            BoundedCapacityOption()
        ],
        builder =>
        {
            AddInputPort(builder, RoutingCompositionPortNames.Input, "Input", "Schema-less JSON request or response value.", nameof(JsonElement), 0, isPrimary: true);
            AddOutputPort(builder, RoutingCompositionPortNames.Output, "Output", "Match or timeout outcome; failures use the message error case.", "FlowCorrelationOutcome<JsonElement>", 1, isPrimary: true);
        },
        [
            RequiredSelectorResource(
                RoutingCompositionResourceNames.KeySelector,
                "Key Selector",
                "Func<JsonElement,string?>",
                0,
                "Required keyed delegate that selects the correlation key for each input message."),
            RequiredSelectorResource(
                RoutingCompositionResourceNames.SideSelector,
                "Side Selector",
                "Func<JsonElement,string?>",
                1,
                "Required keyed delegate that selects request or response side labels."),
            ClockResource(2)
        ],
        new Dictionary<string, string>
        {
            ["requiredResources"] = $"{RoutingCompositionResourceNames.KeySelector},{RoutingCompositionResourceNames.SideSelector}",
            [ComponentDesignMetadataAttributeNames.Aliases] = string.Join(',', RoutingCompositionNodeTypes.CorrelationDescriptor.Aliases)
        });

    private static ComponentDesignMetadata CreateJoinMetadata()
        => RoutingMetadata(
            RoutingCompositionNodeTypes.Join,
            "Join",
            "Joins left and right messages by host-provided key selectors.",
            "combine",
            "join",
            460,
        [
            EngineOption(),
            ExpressionOption(
                "leftKeyExpression",
                "Left Key Expression",
                "Diagnostic left key expression metadata; left keys use the leftKeySelector resource.",
                RoutingCompositionResourceNames.LeftKeySelector),
            ExpressionOption(
                "rightKeyExpression",
                "Right Key Expression",
                "Diagnostic right key expression metadata; right keys use the rightKeySelector resource.",
                RoutingCompositionResourceNames.RightKeySelector),
            ExpressionIdOption(),
            ExpressionNameOption(),
            InputTypeOption("leftInputType", "Left Input Type"),
            InputTypeOption("rightInputType", "Right Input Type"),
            BoolOption("caseSensitive", "Case Sensitive", true, "Match keys using case-sensitive comparisons."),
            NumberOption("timeoutMilliseconds", "Timeout Milliseconds", 30_000, 1, "Timeout for pending joins."),
            NumberOption("maxPending", "Max Pending", 1_024, 1, "Maximum pending join keys."),
            BoundedCapacityOption()
        ],
        builder =>
        {
            AddInputPort(builder, RoutingCompositionPortNames.Left, "Left", "Schema-less JSON left value.", nameof(JsonElement), 0, isPrimary: true);
            AddInputPort(builder, RoutingCompositionPortNames.Right, "Right", "Schema-less JSON right value.", nameof(JsonElement), 1);
            AddOutputPort(builder, RoutingCompositionPortNames.Output, "Output", "Match or timeout outcome; failures use the message error case.", "FlowJoinOutcome<JsonElement,JsonElement>", 2, isPrimary: true);
        },
        [
            RequiredSelectorResource(
                RoutingCompositionResourceNames.LeftKeySelector,
                "Left Key Selector",
                "Func<JsonElement,string?>",
                0,
                "Required keyed delegate that selects the join key for left messages."),
            RequiredSelectorResource(
                RoutingCompositionResourceNames.RightKeySelector,
                "Right Key Selector",
                "Func<JsonElement,string?>",
                1,
                "Required keyed delegate that selects the join key for right messages."),
            ClockResource(2)
        ],
        new Dictionary<string, string>
        {
            ["requiredResources"] = $"{RoutingCompositionResourceNames.LeftKeySelector},{RoutingCompositionResourceNames.RightKeySelector}"
        });

    private static ResourceDesignMetadata RequiredSelectorResource(
        string name,
        string displayName,
        string valueType,
        int order,
        string summary)
        => ResourceDesignMetadataFactory.HostOwned(
            name,
            ResourceDesignMetadataAttributeValues.Delegate,
            displayName,
            order,
            summary,
            valueType,
            isRequired: true,
            keyPattern: "delegate:{name}");

    private static ResourceDesignMetadata ClockResource(int order)
        => ResourceDesignMetadataFactory.Clock(
            RoutingCompositionResourceNames.Clock,
            order,
            "Optional keyed clock for deterministic routing timing, timeouts, and diagnostics.");

    private static OptionDesignMetadata EngineOption()
        => OptionDesignMetadataFactory.Text(
            "engine",
            "Engine",
            "Diagnostic engine metadata; composition DI selection uses host-owned selector resources.",
            "Diagnostics",
            OptionDesignMetadataAttributeValues.Advanced);

    private static OptionDesignMetadata ExpressionOption(
        string name,
        string displayName,
        string helperText,
        string relatedResource)
        => OptionDesignMetadataFactory.Expression(
            name,
            displayName,
            helperText,
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            relatedResource: relatedResource);

    private static OptionDesignMetadata ExpressionIdOption()
        => OptionDesignMetadataFactory.Text(
            "expressionId",
            "Expression ID",
            "Optional diagnostic identifier emitted with routing diagnostics.",
            "Diagnostics",
            OptionDesignMetadataAttributeValues.Advanced);

    private static OptionDesignMetadata ExpressionNameOption()
        => OptionDesignMetadataFactory.Text(
            "expressionName",
            "Expression Name",
            "Optional diagnostic name emitted with routing diagnostics.",
            "Diagnostics",
            OptionDesignMetadataAttributeValues.Advanced);

    private static OptionDesignMetadata InputTypeOption(
        string name,
        string? displayName = null)
        => OptionDesignMetadataFactory.TypeName(
            name,
            displayName ?? "Input Type",
            WindowRoutingOptions.ObjectTypeName,
            "Diagnostic input type metadata; CLR type comes from the closed registration.");

    private static OptionDesignMetadata TextOption(
        string name,
        string displayName,
        string? defaultValue,
        string helperText,
        string section = "Matching")
        => OptionDesignMetadataFactory.Text(
            name,
            displayName,
            helperText,
            section,
            OptionDesignMetadataAttributeValues.Advanced,
            defaultValue);

    private static OptionDesignMetadata BoolOption(
        string name,
        string displayName,
        bool defaultValue,
        string helperText,
        string section = "Matching")
        => OptionDesignMetadataFactory.Boolean(
            name,
            displayName,
            helperText,
            section,
            OptionDesignMetadataAttributeValues.Advanced,
            defaultValue);

    private static OptionDesignMetadata NumberOption(
        string name,
        string displayName,
        int defaultValue,
        double min,
        string helperText,
        string section = "Runtime",
        string importance = OptionDesignMetadataAttributeValues.Advanced)
        => OptionDesignMetadataFactory.Number(
            name,
            displayName,
            helperText,
            section,
            importance,
            defaultValue,
            min);

    private static OptionDesignMetadata BoundedCapacityOption()
        => OptionDesignMetadataFactory.BoundedCapacity(128);

    private static void AddInputPort(
        ComponentDesignMetadataBuilder builder,
        string name,
        string displayName,
        string summary,
        string valueType,
        int order,
        bool isPrimary = false)
        => builder.AddInputPort(
            name,
            displayName: displayName,
            group: "Messages",
            order: order,
            summary: summary,
            valueType: valueType,
            isPrimary: isPrimary);

    private static void AddOutputPort(
        ComponentDesignMetadataBuilder builder,
        string name,
        string displayName,
        string summary,
        string valueType,
        int order,
        bool isPrimary = false)
        => builder.AddOutputPort(
            name,
            displayName: displayName,
            group: "Messages",
            order: order,
            summary: summary,
            valueType: valueType,
            isPrimary: isPrimary);
}
