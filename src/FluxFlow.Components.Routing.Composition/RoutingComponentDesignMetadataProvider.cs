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

    private static ComponentDesignMetadata CreateSwitchMetadata() => new()
    {
        Type = new ComponentType(RoutingCompositionNodeTypes.Switch),
        DisplayName = "Switch",
        Category = "Routing",
        Summary = "Routes input messages by a host-provided route key selector.",
        IconKey = "route",
        PreferredNodeName = "switch",
        SuggestedEditorWidth = 460,
        Options =
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
        Ports =
        [
            InputPort(RoutingCompositionPortNames.Input, "Input", "Input message.", "TInput", 0, isPrimary: true),
            OutputPort(RoutingCompositionPortNames.Output, "Output", "Primary routed output.", "TInput", 1, isPrimary: true),
            OutputPort(RoutingCompositionPortNames.Matched, "Matched", "Input message when a configured route matches.", "TInput", 2),
            OutputPort(RoutingCompositionPortNames.Default, "Default", "Input message when no configured route matches.", "TInput", 3),
            OutputPort(RoutingCompositionPortNames.Routed, "Routed", "Input message when route envelope output is enabled.", "TInput", 4)
        ],
        Attributes = new Dictionary<string, string>
        {
            ["dynamicOutputsOption"] = "routeOutputs",
            ["requiredResource"] = RoutingCompositionResourceNames.RouteKeySelector
        }
    };

    private static ComponentDesignMetadata CreateForkMetadata() => new()
    {
        Type = new ComponentType(RoutingCompositionNodeTypes.Fork),
        DisplayName = "Fork",
        Category = "Routing",
        Summary = "Fans each input message out to configured output ports.",
        IconKey = "git-fork",
        PreferredNodeName = "fork",
        SuggestedEditorWidth = 420,
        Options =
        [
            InputTypeOption("inputType"),
            JsonOption("outputs", "Outputs", "Required dynamic output port names.", isRequired: true),
            BoundedCapacityOption()
        ],
        Ports =
        [
            InputPort(RoutingCompositionPortNames.Input, "Input", "Input message.", "TInput", 0, isPrimary: true),
            OutputPort(RoutingCompositionPortNames.Output, "Output", "Primary output alias.", "TInput", 1, isPrimary: true)
        ],
        Attributes = new Dictionary<string, string>
        {
            ["dynamicOutputsOption"] = "outputs"
        }
    };

    private static ComponentDesignMetadata CreateMergeMetadata() => new()
    {
        Type = new ComponentType(RoutingCompositionNodeTypes.Merge),
        DisplayName = "Merge",
        Category = "Routing",
        Summary = "Merges messages of the same input type into one output stream.",
        IconKey = "merge",
        PreferredNodeName = "merge",
        SuggestedEditorWidth = 400,
        Options =
        [
            InputTypeOption("inputType"),
            BoundedCapacityOption()
        ],
        Ports =
        [
            InputPort(RoutingCompositionPortNames.Input, "Input", "Input message.", "TInput", 0, isPrimary: true),
            OutputPort(RoutingCompositionPortNames.Output, "Output", "Merged output message.", "TInput", 1, isPrimary: true)
        ]
    };

    private static ComponentDesignMetadata CreateWindowMetadata() => new()
    {
        Type = new ComponentType(RoutingCompositionNodeTypes.Window),
        DisplayName = "Window",
        Category = "Routing",
        Summary = "Buffers input messages into count- or time-based windows.",
        IconKey = "panel-top",
        PreferredNodeName = "window",
        SuggestedEditorWidth = 420,
        Options =
        [
            InputTypeOption("inputType"),
            NumberOption("maxItems", "Max Items", 0, 0, "Maximum buffered item count; set timeMilliseconds when zero."),
            NumberOption("timeMilliseconds", "Time Milliseconds", 0, 0, "Maximum window duration in milliseconds; set maxItems when zero."),
            BoolOption("emitPartialOnCompletion", "Emit Partial On Completion", true, "Emit a partial window when input completes."),
            BoundedCapacityOption()
        ],
        Ports =
        [
            InputPort(RoutingCompositionPortNames.Input, "Input", "Input message.", "TInput", 0, isPrimary: true),
            OutputPort(RoutingCompositionPortNames.Output, "Output", "Buffered window.", "FlowWindow<TInput>", 1, isPrimary: true)
        ]
    };

    private static ComponentDesignMetadata CreateCorrelationMetadata() => new()
    {
        Type = new ComponentType(RoutingCompositionNodeTypes.Correlation),
        DisplayName = "Correlation",
        Category = "Routing",
        Summary = "Pairs related request and response messages by host-provided key and side selectors.",
        IconKey = "link",
        PreferredNodeName = "correlate",
        SuggestedEditorWidth = 460,
        Options =
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
        Ports =
        [
            InputPort(RoutingCompositionPortNames.Input, "Input", "Input request or response message.", "TInput", 0, isPrimary: true),
            OutputPort(RoutingCompositionPortNames.Output, "Output", "Correlation match result.", "FlowCorrelationMatch<TInput>", 1, isPrimary: true),
            OutputPort(RoutingCompositionPortNames.Matched, "Matched", "Correlation match result alias.", "FlowCorrelationMatch<TInput>", 2),
            OutputPort(RoutingCompositionPortNames.Timeouts, "Timeouts", "Correlation timeout result.", "FlowCorrelationTimeout<TInput>", 3)
        ],
        Attributes = new Dictionary<string, string>
        {
            ["requiredResources"] = $"{RoutingCompositionResourceNames.KeySelector},{RoutingCompositionResourceNames.SideSelector}"
        }
    };

    private static ComponentDesignMetadata CreateJoinMetadata() => new()
    {
        Type = new ComponentType(RoutingCompositionNodeTypes.Join),
        DisplayName = "Join",
        Category = "Routing",
        Summary = "Joins left and right messages by host-provided key selectors.",
        IconKey = "combine",
        PreferredNodeName = "join",
        SuggestedEditorWidth = 460,
        Options =
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
        Ports =
        [
            InputPort(RoutingCompositionPortNames.Left, "Left", "Left input message.", "TLeft", 0, isPrimary: true),
            InputPort(RoutingCompositionPortNames.Right, "Right", "Right input message.", "TRight", 1),
            OutputPort(RoutingCompositionPortNames.Output, "Output", "Joined output result.", "FlowJoinResult<TLeft,TRight>", 2, isPrimary: true),
            OutputPort(RoutingCompositionPortNames.Timeouts, "Timeouts", "Join timeout result.", "FlowJoinTimeout<TLeft,TRight>", 3)
        ],
        Attributes = new Dictionary<string, string>
        {
            ["requiredResources"] = $"{RoutingCompositionResourceNames.LeftKeySelector},{RoutingCompositionResourceNames.RightKeySelector}"
        }
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

    private static PortDesignMetadata InputPort(
        string name,
        string displayName,
        string summary,
        string valueType,
        int order,
        bool isPrimary = false) => new()
        {
            Name = new ComponentPortName(name),
            Direction = PortDirection.Input,
            DisplayName = displayName,
            Group = "Messages",
            Order = order,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = isPrimary
        };

    private static PortDesignMetadata OutputPort(
        string name,
        string displayName,
        string summary,
        string valueType,
        int order,
        bool isPrimary = false) => new()
        {
            Name = new ComponentPortName(name),
            Direction = PortDirection.Output,
            DisplayName = displayName,
            Group = "Messages",
            Order = order,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = isPrimary
        };
}
