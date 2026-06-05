using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Routing;

public sealed class RoutingComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        Metadata(
            RoutingComponentTypes.Switch,
            "Switch",
            "switch",
            "Routes input values by expression result into named branches.",
            [
                Text("inputType", "Input type", "object"),
                Expression("expression", "Expression", "input != null"),
                Json("routes", "Routes"),
                Json("routeOutputs", "Route outputs"),
                Capacity()
            ],
            [
                Port(RoutingComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(RoutingComponentPorts.Result, PortDirection.Output, "FlowSwitchResult", true, 1),
                Port(RoutingComponentPorts.Default, PortDirection.Output, "Configured input type", false, 2),
                Port(RoutingComponentPorts.Errors, PortDirection.Output, "FlowError", false, 3)
            ]),
        Metadata(
            RoutingComponentTypes.Correlation,
            "Correlation",
            "correlation",
            "Pairs related values by key and side expressions.",
            [
                Text("inputType", "Input type", "object"),
                Expression("keyExpression", "Key expression", "input.topic"),
                Expression("sideExpression", "Side expression", "request"),
                Text("requestSide", "Request side", "request"),
                Text("responseSide", "Response side", "response"),
                Number("timeoutMilliseconds", "Timeout ms", 5000, 1),
                Capacity()
            ],
            [
                Port(RoutingComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(RoutingComponentPorts.Matched, PortDirection.Output, "FlowCorrelationMatch", true, 1),
                Port(RoutingComponentPorts.Timeouts, PortDirection.Output, "FlowCorrelationTimeout", false, 2),
                Port(RoutingComponentPorts.Errors, PortDirection.Output, "FlowError", false, 3)
            ]),
        Metadata(
            RoutingComponentTypes.Window,
            "Window",
            "window",
            "Groups input values into count or time based windows.",
            [
                Text("inputType", "Input type", "object"),
                Number("maxItems", "Max items", 10, 1),
                Number("timeMilliseconds", "Time ms", 1000, 1),
                Boolean("emitPartialOnCompletion", "Emit partial on completion", true),
                Capacity()
            ],
            [
                Port(RoutingComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(RoutingComponentPorts.Output, PortDirection.Output, "FlowWindow", true, 1),
                Port(RoutingComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]),
        Metadata(
            RoutingComponentTypes.Join,
            "Join",
            "join",
            "Pairs left and right streams by matching key expressions.",
            [
                Text("leftInputType", "Left input type", "object"),
                Text("rightInputType", "Right input type", "object"),
                Expression("leftKeyExpression", "Left key expression", "input.key"),
                Expression("rightKeyExpression", "Right key expression", "input.key"),
                Number("timeoutMilliseconds", "Timeout ms", 5000, 1),
                Capacity()
            ],
            [
                Port(RoutingComponentPorts.Left, PortDirection.Input, "Configured left input type", true),
                Port(RoutingComponentPorts.Right, PortDirection.Input, "Configured right input type", true, 1),
                Port(RoutingComponentPorts.Output, PortDirection.Output, "FlowJoinResult", true, 2),
                Port(RoutingComponentPorts.Timeouts, PortDirection.Output, "FlowJoinTimeout", false, 3),
                Port(RoutingComponentPorts.Errors, PortDirection.Output, "FlowError", false, 4)
            ]),
        Metadata(
            RoutingComponentTypes.Fork,
            "Fork",
            "fork",
            "Copies every input value to each configured output branch.",
            [
                Text("inputType", "Input type", "object"),
                Json("outputs", "Outputs"),
                Capacity()
            ],
            [
                Port(RoutingComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port("A", PortDirection.Output, "Configured input type", true, 1),
                Port("B", PortDirection.Output, "Configured input type", false, 2),
                Port(RoutingComponentPorts.Errors, PortDirection.Output, "FlowError", false, 3)
            ]),
        Metadata(
            RoutingComponentTypes.Merge,
            "Merge",
            "merge",
            "Combines same-type input branches into a source-tagged output stream.",
            [
                Text("inputType", "Input type", "object"),
                Json("inputs", "Inputs"),
                Capacity()
            ],
            [
                Port(RoutingComponentPorts.Left, PortDirection.Input, "Configured input type", true),
                Port(RoutingComponentPorts.Right, PortDirection.Input, "Configured input type", false, 1),
                Port(RoutingComponentPorts.Output, PortDirection.Output, "FlowMergeItem", true, 2),
                Port(RoutingComponentPorts.Errors, PortDirection.Output, "FlowError", false, 3)
            ])
    ];

    private static ComponentDesignMetadata Metadata(
        NodeType type,
        string displayName,
        string preferredName,
        string summary,
        IReadOnlyList<OptionDesignMetadata> options,
        IReadOnlyList<PortDesignMetadata> ports) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "Routing",
            Summary = summary,
            IconKey = "routing",
            PreferredNodeName = preferredName,
            SuggestedEditorWidth = 560,
            Options = options,
            Ports = ports
        };

    private static OptionDesignMetadata Text(string name, string displayName, object defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static OptionDesignMetadata Expression(string name, string displayName, object defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Expression,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static OptionDesignMetadata Json(string name, string displayName) => new()
    {
        Name = name,
        Kind = OptionValueKind.Json,
        DisplayName = displayName
    };

    private static OptionDesignMetadata Number(string name, string displayName, object defaultValue, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static OptionDesignMetadata Boolean(string name, string displayName, bool defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Boolean,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static OptionDesignMetadata Capacity() => Number("boundedCapacity", "Capacity", 128, 1);

    private static PortDesignMetadata Port(
        string name,
        PortDirection direction,
        string valueType,
        bool primary,
        int order = 0) => new()
        {
            Name = new PortName(name),
            Direction = direction,
            ValueType = valueType,
            IsPrimary = primary,
            Order = order
        };
}
