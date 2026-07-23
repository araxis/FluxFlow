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

    public static CompositionNodeRegistry RegisterTimerDelay(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Delay)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateTimerDelayNode,
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

    public static CompositionNodeRegistry RegisterTimerThrottle(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Throttle)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateTimerThrottleNode,
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

    public static CompositionNodeRegistry RegisterTimerDebounce(
        this CompositionNodeRegistry registry,
        string nodeType = TimersCompositionNodeTypes.Debounce)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateTimerDebounceNode,
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
        var node = new TimerIntervalNode(settings, clock);

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
        var node = new TimerScheduleNode(settings, clock);

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

    private static ValueTask<ComposedNode> CreateTimerDelayNode(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerDelaySettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new TimerDelayNode(settings, clock);

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

    private static ValueTask<ComposedNode> CreateTimerThrottleNode(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerThrottleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new TimerThrottleNode(settings, clock);

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

    private static ValueTask<ComposedNode> CreateTimerDebounceNode(
        CompositionNodeFactoryContext context)
    {
        var settings = context.BindConfiguration<TimerDebounceSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersCompositionResourceNames.Clock);
        var node = new TimerDebounceNode(settings, clock);

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

}
