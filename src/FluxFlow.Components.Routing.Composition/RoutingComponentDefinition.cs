using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Routing.Options;

namespace FluxFlow.Components.Routing.Composition;

public static partial class RoutingComponentDefinition
{
    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
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
            RoutingComponentDefinition.Types.Window,
            "Window",
            "Buffers input messages into count- or time-based windows.",
            "panel-top",
            "window",
            420,
        [
            InputTypeOption(Options.InputType),
            NumberOption(
                Options.MaxItems,
                "Max Items",
                0,
                0,
                "Maximum buffered item count; set timeMilliseconds when zero.",
                "Windowing",
                OptionDesignMetadataAttributeValues.Primary),
            NumberOption(
                Options.TimeMilliseconds,
                "Time Milliseconds",
                0,
                0,
                "Maximum window duration in milliseconds; set maxItems when zero.",
                "Windowing",
                OptionDesignMetadataAttributeValues.Primary),
            BoolOption(
                Options.EmitPartialOnCompletion,
                "Emit Partial On Completion",
                true,
                "Emit a partial window when input completes.",
                "Windowing"),
            BoundedCapacityOption()
        ],
        builder =>
        {
            AddInputPort(builder, RoutingComponentDefinition.Ports.Input, Ports.Input, "Schema-less JSON value.", nameof(JsonElement), 0, isPrimary: true);
            AddOutputPort(builder, RoutingComponentDefinition.Ports.Output, Ports.Output, "Count-, time-, or completion-based window; failures use the message error case.", "FlowWindow<JsonElement>", 1, isPrimary: true);
        },
        [ClockResource(0)]);

    private static ComponentDesignMetadata CreateCorrelationMetadata()
        => RoutingMetadata(
            RoutingComponentDefinition.Types.Correlation,
            "Correlation",
            "Pairs related request and response messages by host-provided key and side selectors.",
            "link",
            "correlate",
            460,
        [
            EngineOption(),
            ExpressionOption(
                Options.KeyExpression,
                "Key Expression",
                "Diagnostic key expression metadata; key selection uses the keySelector resource.",
                RoutingComponentDefinition.Resources.KeySelector),
            ExpressionOption(
                Options.SideExpression,
                "Side Expression",
                "Diagnostic side expression metadata; side selection uses the sideSelector resource.",
                RoutingComponentDefinition.Resources.SideSelector),
            ExpressionIdOption(),
            ExpressionNameOption(),
            InputTypeOption(Options.InputType),
            TextOption(Options.RequestSide, "Request Side", "request", "Side label treated as the request side."),
            TextOption(Options.ResponseSide, "Response Side", "response", "Side label treated as the response side."),
            BoolOption(Options.CaseSensitive, "Case Sensitive", true, "Match keys and sides using case-sensitive comparisons."),
            NumberOption(Options.TimeoutMilliseconds, "Timeout Milliseconds", 30_000, 1, "Timeout for pending correlations."),
            NumberOption(Options.MaxPending, "Max Pending", 1_024, 1, "Maximum pending correlation keys."),
            BoundedCapacityOption()
        ],
        builder =>
        {
            AddInputPort(builder, RoutingComponentDefinition.Ports.Input, Ports.Input, "Schema-less JSON request or response value.", nameof(JsonElement), 0, isPrimary: true);
            AddOutputPort(builder, RoutingComponentDefinition.Ports.Output, Ports.Output, "Match or timeout outcome; failures use the message error case.", "FlowCorrelationOutcome<JsonElement>", 1, isPrimary: true);
        },
        [
            RequiredSelectorResource(
                RoutingComponentDefinition.Resources.KeySelector,
                "Key Selector",
                "Func<JsonElement,string?>",
                0,
                "Required keyed delegate that selects the correlation key for each input message."),
            RequiredSelectorResource(
                RoutingComponentDefinition.Resources.SideSelector,
                "Side Selector",
                "Func<JsonElement,string?>",
                1,
                "Required keyed delegate that selects request or response side labels."),
            ClockResource(2)
        ],
        new Dictionary<string, string>
        {
            ["requiredResources"] = $"{RoutingComponentDefinition.Resources.KeySelector},{RoutingComponentDefinition.Resources.SideSelector}"
        });

    private static ComponentDesignMetadata CreateJoinMetadata()
        => RoutingMetadata(
            RoutingComponentDefinition.Types.Join,
            "Join",
            "Joins left and right messages by host-provided key selectors.",
            "combine",
            "join",
            460,
        [
            EngineOption(),
            ExpressionOption(
                Options.LeftKeyExpression,
                "Left Key Expression",
                "Diagnostic left key expression metadata; left keys use the leftKeySelector resource.",
                RoutingComponentDefinition.Resources.LeftKeySelector),
            ExpressionOption(
                Options.RightKeyExpression,
                "Right Key Expression",
                "Diagnostic right key expression metadata; right keys use the rightKeySelector resource.",
                RoutingComponentDefinition.Resources.RightKeySelector),
            ExpressionIdOption(),
            ExpressionNameOption(),
            InputTypeOption(Options.LeftInputType, "Left Input Type"),
            InputTypeOption(Options.RightInputType, "Right Input Type"),
            BoolOption(Options.CaseSensitive, "Case Sensitive", true, "Match keys using case-sensitive comparisons."),
            NumberOption(Options.TimeoutMilliseconds, "Timeout Milliseconds", 30_000, 1, "Timeout for pending joins."),
            NumberOption(Options.MaxPending, "Max Pending", 1_024, 1, "Maximum pending join keys."),
            BoundedCapacityOption()
        ],
        builder =>
        {
            AddInputPort(builder, RoutingComponentDefinition.Ports.Left, Ports.Left, "Schema-less JSON left value.", nameof(JsonElement), 0, isPrimary: true);
            AddInputPort(builder, RoutingComponentDefinition.Ports.Right, Ports.Right, "Schema-less JSON right value.", nameof(JsonElement), 1);
            AddOutputPort(builder, RoutingComponentDefinition.Ports.Output, Ports.Output, "Match or timeout outcome; failures use the message error case.", "FlowJoinOutcome<JsonElement,JsonElement>", 2, isPrimary: true);
        },
        [
            RequiredSelectorResource(
                RoutingComponentDefinition.Resources.LeftKeySelector,
                "Left Key Selector",
                "Func<JsonElement,string?>",
                0,
                "Required keyed delegate that selects the join key for left messages."),
            RequiredSelectorResource(
                RoutingComponentDefinition.Resources.RightKeySelector,
                "Right Key Selector",
                "Func<JsonElement,string?>",
                1,
                "Required keyed delegate that selects the join key for right messages."),
            ClockResource(2)
        ],
        new Dictionary<string, string>
        {
            ["requiredResources"] = $"{RoutingComponentDefinition.Resources.LeftKeySelector},{RoutingComponentDefinition.Resources.RightKeySelector}"
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
            RoutingComponentDefinition.Resources.Clock,
            order,
            "Optional keyed clock for deterministic routing timing, timeouts, and diagnostics.");

    private static OptionDesignMetadata EngineOption()
        => OptionDesignMetadataFactory.Text(
            Options.Engine,
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
            Options.ExpressionId,
            "Expression ID",
            "Optional diagnostic identifier emitted with routing diagnostics.",
            "Diagnostics",
            OptionDesignMetadataAttributeValues.Advanced);

    private static OptionDesignMetadata ExpressionNameOption()
        => OptionDesignMetadataFactory.Text(
            Options.ExpressionName,
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


    public static class Options
    {
        public const string InputType = "inputType";
        public const string MaxItems = "maxItems";
        public const string TimeMilliseconds = "timeMilliseconds";
        public const string EmitPartialOnCompletion = "emitPartialOnCompletion";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Engine = "engine";
        public const string KeyExpression = "keyExpression";
        public const string SideExpression = "sideExpression";
        public const string ExpressionId = "expressionId";
        public const string ExpressionName = "expressionName";
        public const string RequestSide = "requestSide";
        public const string ResponseSide = "responseSide";
        public const string CaseSensitive = "caseSensitive";
        public const string TimeoutMilliseconds = "timeoutMilliseconds";
        public const string MaxPending = "maxPending";
        public const string LeftKeyExpression = "leftKeyExpression";
        public const string RightKeyExpression = "rightKeyExpression";
        public const string LeftInputType = "leftInputType";
        public const string RightInputType = "rightInputType";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Window =>
            [
                ComponentOptions.Metadata<string>(Options.InputType),
                ComponentOptions.Metadata<int>(Options.MaxItems),
                ComponentOptions.Metadata<int>(Options.TimeMilliseconds),
                ComponentOptions.Metadata<bool>(Options.EmitPartialOnCompletion),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Correlation =>
            [
                ComponentOptions.Metadata<string>(Options.Engine),
                ComponentOptions.Metadata<string>(Options.KeyExpression),
                ComponentOptions.Metadata<string>(Options.SideExpression),
                ComponentOptions.Metadata<string>(Options.ExpressionId),
                ComponentOptions.Metadata<string>(Options.ExpressionName),
                ComponentOptions.Metadata<string>(Options.InputType),
                ComponentOptions.Metadata<string>(Options.RequestSide),
                ComponentOptions.Metadata<string>(Options.ResponseSide),
                ComponentOptions.Metadata<bool>(Options.CaseSensitive),
                ComponentOptions.Metadata<int>(Options.TimeoutMilliseconds),
                ComponentOptions.Metadata<int>(Options.MaxPending),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Join =>
            [
                ComponentOptions.Metadata<string>(Options.Engine),
                ComponentOptions.Metadata<string>(Options.LeftKeyExpression),
                ComponentOptions.Metadata<string>(Options.RightKeyExpression),
                ComponentOptions.Metadata<string>(Options.ExpressionId),
                ComponentOptions.Metadata<string>(Options.ExpressionName),
                ComponentOptions.Metadata<string>(Options.LeftInputType),
                ComponentOptions.Metadata<string>(Options.RightInputType),
                ComponentOptions.Metadata<bool>(Options.CaseSensitive),
                ComponentOptions.Metadata<int>(Options.TimeoutMilliseconds),
                ComponentOptions.Metadata<int>(Options.MaxPending),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Window =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Correlation =>
            [
                ComponentResources.Metadata<Func<JsonElement, string?>>(Resources.KeySelector, isRequired: true),
                ComponentResources.Metadata<Func<JsonElement, string?>>(Resources.SideSelector, isRequired: true),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Join =>
            [
                ComponentResources.Metadata<Func<JsonElement, string?>>(Resources.LeftKeySelector, isRequired: true),
                ComponentResources.Metadata<Func<JsonElement, string?>>(Resources.RightKeySelector, isRequired: true),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Window = "flow.window";
    
        public const string Correlation = "flow.correlate";
        public const string Join = "flow.join";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    
        public const string Left = "Left";
    
        public const string Right = "Right";
    }

    public static class Resources
    {
        public const string Clock = "clock";
    
        public const string KeySelector = "keySelector";
    
        public const string SideSelector = "sideSelector";
    
        public const string LeftKeySelector = "leftKeySelector";
    
        public const string RightKeySelector = "rightKeySelector";
    }
}
