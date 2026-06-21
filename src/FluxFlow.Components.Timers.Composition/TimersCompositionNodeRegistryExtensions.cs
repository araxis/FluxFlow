using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;

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
                CompositionPorts.Metadata<TimerTick>(
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
                CompositionPorts.Metadata<ScheduleTick>(
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
                CompositionPorts.Output<TimerTick>(
                    TimersCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
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
                CompositionPorts.Output<ScheduleTick>(
                    TimersCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
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
