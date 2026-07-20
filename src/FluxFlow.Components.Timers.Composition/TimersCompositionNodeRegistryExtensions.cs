using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Timers.Composition;

public static class TimersCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterTimerInterval(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Interval)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateTimerIntervalNode,
            outputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    TimersCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterTimerSchedule(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Schedule)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateTimerScheduleNode,
            outputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    TimersCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterTimerDelay<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Delay)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateTimerDelayNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    TimersCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<TInput>(
                    TimersCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterTimerDelay(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Delay)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFlowValueTimerDelayNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    TimersCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowValue>>(
                    TimersCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterTimerThrottle<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Throttle)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateTimerThrottleNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    TimersCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<TInput>(
                    TimersCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterTimerThrottle(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Throttle)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFlowValueTimerThrottleNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    TimersCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowValue>>(
                    TimersCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterTimerDebounce<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Debounce)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateTimerDebounceNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    TimersCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<TInput>(
                    TimersCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterTimerDebounce(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Debounce)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFlowValueTimerDebounceNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    TimersCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowValue>>(
                    TimersCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateTimerIntervalNode(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerIntervalSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new FlowValueTimerIntervalNode(settings, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<FlowValue>(
                    TimersCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateTimerScheduleNode(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerScheduleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new FlowValueTimerScheduleNode(settings, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<FlowValue>(
                    TimersCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateFlowValueTimerDelayNode(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerDelaySettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new FlowValueTimerDelayNode(settings, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    TimersCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowValue>>(
                    TimersCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateFlowValueTimerThrottleNode(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerThrottleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new FlowValueTimerThrottleNode(settings, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    TimersCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowValue>>(
                    TimersCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateFlowValueTimerDebounceNode(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerDebounceSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new FlowValueTimerDebounceNode(settings, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    TimersCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowValue>>(
                    TimersCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateTimerDelayNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerDelaySettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new TimerDelayNode<TInput>(settings, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    TimersCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<TInput>(
                    TimersCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateTimerThrottleNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerThrottleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new TimerThrottleNode<TInput>(settings, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    TimersCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<TInput>(
                    TimersCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateTimerDebounceNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerDebounceSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new TimerDebounceNode<TInput>(settings, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    TimersCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<TInput>(
                    TimersCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
