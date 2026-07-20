using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Timers.Composition;

public static class TimersTypedRegistrationExtensions
{
    public static CompositionNodeRegistry RegisterTimerIntervalTicks(
        this CompositionNodeRegistry registry,
        string nodeType)
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

    public static CompositionNodeRegistry RegisterTimerScheduleTicks(
        this CompositionNodeRegistry registry,
        string nodeType)
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
}
