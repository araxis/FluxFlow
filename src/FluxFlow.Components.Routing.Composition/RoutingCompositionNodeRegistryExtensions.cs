using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingCompositionNodeRegistryExtensions
{
    private static readonly string[] ReservedDynamicOutputPorts =
    [
        RoutingCompositionPortNames.Input,
        RoutingCompositionPortNames.Output,
        RoutingCompositionPortNames.Matched,
        RoutingCompositionPortNames.Default,
        RoutingCompositionPortNames.Routed,
        RoutingCompositionPortNames.Timeouts,
        RoutingCompositionPortNames.Left,
        RoutingCompositionPortNames.Right,
        "Errors",
        "Events"
    ];

    public static CompositionNodeRegistry RegisterSwitch<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Switch)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateSwitchNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    RoutingCompositionPortNames.Input)
            ]);
    }

    public static CompositionNodeRegistry RegisterFork<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Fork)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateForkNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    RoutingCompositionPortNames.Input)
            ]);
    }

    public static CompositionNodeRegistry RegisterMerge<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Merge)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateMergeNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    RoutingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<TInput>(
                    RoutingCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterWindow<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Window)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateWindowNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    RoutingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowWindow<TInput>>(
                    RoutingCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterCorrelation<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Correlation)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateCorrelationNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    RoutingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowCorrelationMatch<TInput>>(
                    RoutingCompositionPortNames.Output),
                CompositionPorts.Metadata<FlowCorrelationMatch<TInput>>(
                    RoutingCompositionPortNames.Matched),
                CompositionPorts.Metadata<FlowCorrelationTimeout<TInput>>(
                    RoutingCompositionPortNames.Timeouts)
            ]);
    }

    public static CompositionNodeRegistry RegisterJoin<TLeft, TRight>(
        this CompositionNodeRegistry registry,
        string nodeType = RoutingCompositionNodeTypes.Join)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateJoinNode<TLeft, TRight>,
            inputs:
            [
                CompositionPorts.Metadata<TLeft>(
                    RoutingCompositionPortNames.Left),
                CompositionPorts.Metadata<TRight>(
                    RoutingCompositionPortNames.Right)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowJoinResult<TLeft, TRight>>(
                    RoutingCompositionPortNames.Output),
                CompositionPorts.Metadata<FlowJoinTimeout<TLeft, TRight>>(
                    RoutingCompositionPortNames.Timeouts)
            ]);
    }

    private static ValueTask<ComposedNode> CreateSwitchNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SwitchRoutingOptions>();
        options = options with
        {
            RouteOutputs = ValidateRouteOutputs(options.RouteOutputs)
        };
        var selector = context.GetRequiredResource<Func<TInput, string?>>(
            RoutingCompositionResourceNames.RouteKeySelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowSwitchNode<TInput>(
            options,
            selector,
            options.Engine,
            clock);
        var outputs = new List<CompositionOutputPort>
        {
            CompositionPorts.Output<TInput>(
                RoutingCompositionPortNames.Output,
                node.Output),
            CompositionPorts.Output<TInput>(
                RoutingCompositionPortNames.Matched,
                node.Matched),
            CompositionPorts.Output<TInput>(
                RoutingCompositionPortNames.Default,
                node.Default)
        };

        if (node.Routed is not null)
        {
            outputs.Add(CompositionPorts.Output<TInput>(
                RoutingCompositionPortNames.Routed,
                node.Routed));
        }

        foreach (var (portName, port) in node.RouteOutputs)
        {
            outputs.Add(CompositionPorts.Output<TInput>(portName, port));
        }

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs: outputs,
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateForkNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<ForkRoutingOptions>();
        options = options with
        {
            Outputs = ValidateOutputNames(
                RoutingCompositionNodeTypes.Fork,
                "outputs",
                options.Outputs,
                requireAtLeastOne: true)
        };
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowForkNode<TInput>(options, clock);
        var outputs = new List<CompositionOutputPort>
        {
            CompositionPorts.Output<TInput>(
                RoutingCompositionPortNames.Output,
                node.Output)
        };

        foreach (var (portName, port) in node.Outputs)
        {
            outputs.Add(CompositionPorts.Output<TInput>(portName, port));
        }

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs: outputs,
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateMergeNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<MergeRoutingOptions>();
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowMergeNode<TInput>(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<TInput>(
                    RoutingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateWindowNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<WindowRoutingOptions>();
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowWindowNode<TInput>(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowWindow<TInput>>(
                    RoutingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateCorrelationNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<CorrelationRoutingOptions>();
        var keySelector = context.GetRequiredResource<Func<TInput, string?>>(
            RoutingCompositionResourceNames.KeySelector);
        var sideSelector = context.GetRequiredResource<Func<TInput, string?>>(
            RoutingCompositionResourceNames.SideSelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowCorrelationNode<TInput>(
            options,
            keySelector,
            sideSelector,
            options.Engine,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    RoutingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowCorrelationMatch<TInput>>(
                    RoutingCompositionPortNames.Output,
                    node.Output),
                CompositionPorts.Output<FlowCorrelationMatch<TInput>>(
                    RoutingCompositionPortNames.Matched,
                    node.Matched),
                CompositionPorts.Output<FlowCorrelationTimeout<TInput>>(
                    RoutingCompositionPortNames.Timeouts,
                    node.Timeouts)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateJoinNode<TLeft, TRight>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<JoinRoutingOptions>();
        var leftSelector = context.GetRequiredResource<Func<TLeft, string?>>(
            RoutingCompositionResourceNames.LeftKeySelector);
        var rightSelector = context.GetRequiredResource<Func<TRight, string?>>(
            RoutingCompositionResourceNames.RightKeySelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingCompositionResourceNames.Clock);
        var node = new FlowJoinNode<TLeft, TRight>(
            options,
            leftSelector,
            rightSelector,
            options.Engine,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TLeft>(
                    RoutingCompositionPortNames.Left,
                    node.Left),
                CompositionPorts.Input<TRight>(
                    RoutingCompositionPortNames.Right,
                    node.Right)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowJoinResult<TLeft, TRight>>(
                    RoutingCompositionPortNames.Output,
                    node.Output),
                CompositionPorts.Output<FlowJoinTimeout<TLeft, TRight>>(
                    RoutingCompositionPortNames.Timeouts,
                    node.Timeouts)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static Dictionary<string, string> ValidateRouteOutputs(
        Dictionary<string, string> routeOutputs)
    {
        if (routeOutputs.Count == 0)
            return routeOutputs;

        var outputNames = ValidateOutputNames(
            RoutingCompositionNodeTypes.Switch,
            "routeOutputs",
            routeOutputs.Values.ToArray(),
            requireAtLeastOne: false);
        var index = 0;
        var sanitized = new Dictionary<string, string>(
            routeOutputs.Count,
            StringComparer.Ordinal);
        foreach (var (route, _) in routeOutputs)
        {
            sanitized[route.Trim()] = outputNames[index++];
        }

        return sanitized;
    }

    private static string[] ValidateOutputNames(
        string nodeType,
        string optionName,
        IReadOnlyCollection<string> portNames,
        bool requireAtLeastOne)
    {
        if (requireAtLeastOne && portNames.Count == 0)
        {
            throw new ArgumentException(
                $"{nodeType} option '{optionName}' must contain at least one value.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reserved = ReservedDynamicOutputPorts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>(portNames.Count);
        foreach (var portName in portNames)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new ArgumentException(
                    $"{nodeType} option '{optionName}' cannot contain empty values.");
            }

            var value = portName.Trim();
            if (!seen.Add(value))
            {
                throw new ArgumentException(
                    $"{nodeType} option '{optionName}' contains duplicate output port '{value}'.");
            }

            if (reserved.Contains(value))
            {
                throw new ArgumentException(
                    $"{nodeType} option '{optionName}' cannot use built-in output port '{value}'.");
            }

            if (!IsValidPortName(value))
            {
                throw new ArgumentException(
                    $"{nodeType} option '{optionName}' contains invalid output port '{value}'.");
            }

            normalized.Add(value);
        }

        return normalized.ToArray();
    }

    private static bool IsValidPortName(string value)
    {
        if (!(char.IsLetter(value[0]) || value[0] == '_'))
            return false;

        foreach (var character in value)
        {
            if (!(char.IsLetterOrDigit(character) || character == '_'))
                return false;
        }

        return true;
    }
}
