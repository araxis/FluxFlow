using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Routing.Options;

namespace FluxFlow.Components.Routing.Composition;

public sealed class RoutingComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateSwitchMetadata(),
            CreateForkMetadata(),
            CreateMergeMetadata(),
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

    private static ComponentDesignMetadata CreateSwitchMetadata()
        => RoutingMetadata(
            RoutingCompositionNodeTypes.Switch,
            "Switch",
            "Routes input messages by a host-provided route key selector.",
            "route",
            "switch",
            460,
        [
            EngineOption(),
            ExpressionOption("expression", "Expression", "Diagnostic expression metadata; route selection uses the routeKeySelector resource."),
            ExpressionIdOption(),
            ExpressionNameOption(),
            InputTypeOption("inputType"),
            JsonOption("routes", "Routes", "Optional known route keys used for matching."),
            JsonOption("routeOutputs", "Route Outputs", "Optional mapping of route key to dynamic output port name."),
            new OptionDesignMetadata
            {
                Name = "defaultRoute",
                Kind = OptionValueKind.Text,
                DisplayName = "Default Route",
                HelperText = "Optional route name used when the selector returns no configured route."
            },
            BoolOption("caseSensitive", "Case Sensitive", true, "Match route keys using case-sensitive comparisons."),
            BoolOption("emitMatchedInput", "Emit Matched Input", true, "Emit matched input messages on the Matched output."),
            BoolOption("emitDefaultInput", "Emit Default Input", true, "Emit default-routed messages on the Default output."),
            BoolOption("emitRouteEnvelope", "Emit Route Envelope", false, "Emit routed messages on the Routed output."),
            BoundedCapacityOption()
        ],
        builder =>
        {
            AddInputPort(builder, RoutingCompositionPortNames.Input, "Input", "Input message.", "TInput", 0, isPrimary: true);
            AddOutputPort(builder, RoutingCompositionPortNames.Output, "Output", "Primary routed output.", "TInput", 1, isPrimary: true);
            AddOutputPort(builder, RoutingCompositionPortNames.Matched, "Matched", "Input message when a configured route matches.", "TInput", 2);
            AddOutputPort(builder, RoutingCompositionPortNames.Default, "Default", "Input message when no configured route matches.", "TInput", 3);
            AddOutputPort(builder, RoutingCompositionPortNames.Routed, "Routed", "Input message when route envelope output is enabled.", "TInput", 4);
        },
        [
            RequiredSelectorResource(
                RoutingCompositionResourceNames.RouteKeySelector,
                "Route Key Selector",
                "Func<TInput,string?>",
                0,
                "Required keyed delegate that selects the route key for each input message."),
            ClockResource(1)
        ],
        new Dictionary<string, string>
        {
            ["dynamicOutputsOption"] = "routeOutputs",
            ["requiredResource"] = RoutingCompositionResourceNames.RouteKeySelector
        });

    private static ComponentDesignMetadata CreateForkMetadata()
        => RoutingMetadata(
            RoutingCompositionNodeTypes.Fork,
            "Fork",
            "Fans each input message out to configured output ports.",
            "git-fork",
            "fork",
            420,
        [
            InputTypeOption("inputType"),
            JsonOption("outputs", "Outputs", "Required dynamic output port names.", isRequired: true),
            BoundedCapacityOption()
        ],
        builder =>
        {
            AddInputPort(builder, RoutingCompositionPortNames.Input, "Input", "Input message.", "TInput", 0, isPrimary: true);
            AddOutputPort(builder, RoutingCompositionPortNames.Output, "Output", "Primary output alias.", "TInput", 1, isPrimary: true);
        },
        [ClockResource(0)],
        new Dictionary<string, string>
        {
            ["dynamicOutputsOption"] = "outputs"
        });

    private static ComponentDesignMetadata CreateMergeMetadata()
        => RoutingMetadata(
            RoutingCompositionNodeTypes.Merge,
            "Merge",
            "Merges messages of the same input type into one output stream.",
            "merge",
            "merge",
            400,
        [
            InputTypeOption("inputType"),
            BoundedCapacityOption()
        ],
        builder =>
        {
            AddInputPort(builder, RoutingCompositionPortNames.Input, "Input", "Input message.", "TInput", 0, isPrimary: true);
            AddOutputPort(builder, RoutingCompositionPortNames.Output, "Output", "Merged output message.", "TInput", 1, isPrimary: true);
        },
        [ClockResource(0)]);

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
            NumberOption("maxItems", "Max Items", 0, 0, "Maximum buffered item count; set timeMilliseconds when zero."),
            NumberOption("timeMilliseconds", "Time Milliseconds", 0, 0, "Maximum window duration in milliseconds; set maxItems when zero."),
            BoolOption("emitPartialOnCompletion", "Emit Partial On Completion", true, "Emit a partial window when input completes."),
            BoundedCapacityOption()
        ],
        builder =>
        {
            AddInputPort(builder, RoutingCompositionPortNames.Input, "Input", "Input message.", "TInput", 0, isPrimary: true);
            AddOutputPort(builder, RoutingCompositionPortNames.Output, "Output", "Buffered window.", "FlowWindow<TInput>", 1, isPrimary: true);
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
            ExpressionOption("keyExpression", "Key Expression", "Diagnostic key expression metadata; key selection uses the keySelector resource."),
            ExpressionOption("sideExpression", "Side Expression", "Diagnostic side expression metadata; side selection uses the sideSelector resource."),
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
            AddInputPort(builder, RoutingCompositionPortNames.Input, "Input", "Input request or response message.", "TInput", 0, isPrimary: true);
            AddOutputPort(builder, RoutingCompositionPortNames.Output, "Output", "Correlation match result.", "FlowCorrelationMatch<TInput>", 1, isPrimary: true);
            AddOutputPort(builder, RoutingCompositionPortNames.Matched, "Matched", "Correlation match result alias.", "FlowCorrelationMatch<TInput>", 2);
            AddOutputPort(builder, RoutingCompositionPortNames.Timeouts, "Timeouts", "Correlation timeout result.", "FlowCorrelationTimeout<TInput>", 3);
        },
        [
            RequiredSelectorResource(
                RoutingCompositionResourceNames.KeySelector,
                "Key Selector",
                "Func<TInput,string?>",
                0,
                "Required keyed delegate that selects the correlation key for each input message."),
            RequiredSelectorResource(
                RoutingCompositionResourceNames.SideSelector,
                "Side Selector",
                "Func<TInput,string?>",
                1,
                "Required keyed delegate that selects request or response side labels."),
            ClockResource(2)
        ],
        new Dictionary<string, string>
        {
            ["requiredResources"] = $"{RoutingCompositionResourceNames.KeySelector},{RoutingCompositionResourceNames.SideSelector}"
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
            ExpressionOption("leftKeyExpression", "Left Key Expression", "Diagnostic left key expression metadata; left keys use the leftKeySelector resource."),
            ExpressionOption("rightKeyExpression", "Right Key Expression", "Diagnostic right key expression metadata; right keys use the rightKeySelector resource."),
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
            AddInputPort(builder, RoutingCompositionPortNames.Left, "Left", "Left input message.", "TLeft", 0, isPrimary: true);
            AddInputPort(builder, RoutingCompositionPortNames.Right, "Right", "Right input message.", "TRight", 1);
            AddOutputPort(builder, RoutingCompositionPortNames.Output, "Output", "Joined output result.", "FlowJoinResult<TLeft,TRight>", 2, isPrimary: true);
            AddOutputPort(builder, RoutingCompositionPortNames.Timeouts, "Timeouts", "Join timeout result.", "FlowJoinTimeout<TLeft,TRight>", 3);
        },
        [
            RequiredSelectorResource(
                RoutingCompositionResourceNames.LeftKeySelector,
                "Left Key Selector",
                "Func<TLeft,string?>",
                0,
                "Required keyed delegate that selects the join key for left messages."),
            RequiredSelectorResource(
                RoutingCompositionResourceNames.RightKeySelector,
                "Right Key Selector",
                "Func<TRight,string?>",
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
        string summary) => new()
        {
            Name = new ComponentResourceName(name),
            DisplayName = displayName,
            Order = order,
            Summary = summary,
            ValueType = valueType,
            IsRequired = true
        };

    private static ResourceDesignMetadata ClockResource(int order) => new()
    {
        Name = new ComponentResourceName(RoutingCompositionResourceNames.Clock),
        DisplayName = "Clock",
        Order = order,
        Summary = "Optional keyed clock for deterministic routing timing, timeouts, and diagnostics.",
        ValueType = nameof(TimeProvider)
    };

    private static OptionDesignMetadata EngineOption() => new()
    {
        Name = "engine",
        Kind = OptionValueKind.Text,
        DisplayName = "Engine",
        HelperText = "Diagnostic engine metadata; composition DI selection uses host-owned selector resources."
    };

    private static OptionDesignMetadata ExpressionOption(
        string name,
        string displayName,
        string helperText) => new()
        {
            Name = name,
            Kind = OptionValueKind.Expression,
            DisplayName = displayName,
            HelperText = helperText
        };

    private static OptionDesignMetadata ExpressionIdOption() => new()
    {
        Name = "expressionId",
        Kind = OptionValueKind.Text,
        DisplayName = "Expression ID",
        HelperText = "Optional diagnostic identifier emitted with routing diagnostics."
    };

    private static OptionDesignMetadata ExpressionNameOption() => new()
    {
        Name = "expressionName",
        Kind = OptionValueKind.Text,
        DisplayName = "Expression Name",
        HelperText = "Optional diagnostic name emitted with routing diagnostics."
    };

    private static OptionDesignMetadata InputTypeOption(
        string name,
        string? displayName = null) => new()
        {
            Name = name,
            Kind = OptionValueKind.Text,
            DisplayName = displayName ?? "Input Type",
            DefaultValue = SwitchRoutingOptions.ObjectTypeName,
            HelperText = "Diagnostic input type metadata; CLR type comes from the closed registration."
        };

    private static OptionDesignMetadata JsonOption(
        string name,
        string displayName,
        string helperText,
        bool isRequired = false) => new()
        {
            Name = name,
            Kind = OptionValueKind.Json,
            DisplayName = displayName,
            HelperText = helperText,
            IsRequired = isRequired
        };

    private static OptionDesignMetadata TextOption(
        string name,
        string displayName,
        string defaultValue,
        string helperText) => new()
        {
            Name = name,
            Kind = OptionValueKind.Text,
            DisplayName = displayName,
            DefaultValue = defaultValue,
            HelperText = helperText
        };

    private static OptionDesignMetadata BoolOption(
        string name,
        string displayName,
        bool defaultValue,
        string helperText) => new()
        {
            Name = name,
            Kind = OptionValueKind.Boolean,
            DisplayName = displayName,
            DefaultValue = defaultValue,
            HelperText = helperText
        };

    private static OptionDesignMetadata NumberOption(
        string name,
        string displayName,
        int defaultValue,
        double min,
        string helperText) => new()
        {
            Name = name,
            Kind = OptionValueKind.Number,
            DisplayName = displayName,
            DefaultValue = defaultValue,
            Min = min,
            HelperText = helperText
        };

    private static OptionDesignMetadata BoundedCapacityOption() => new()
    {
        Name = "boundedCapacity",
        Kind = OptionValueKind.Number,
        DisplayName = "Bounded Capacity",
        DefaultValue = 128,
        Min = 1,
        HelperText = "Maximum queued input messages."
    };

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
