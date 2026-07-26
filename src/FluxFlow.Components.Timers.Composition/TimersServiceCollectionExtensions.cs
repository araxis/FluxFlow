using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Timers.Composition;

public static class TimersServiceCollectionExtensions
{
    internal static ComponentDescriptor IntervalDescriptor { get; } = CreateSourceDescriptor<TimerIntervalTick>(
        TimersComponentTypes.Interval,
        CreateTimerIntervalNode);
    internal static ComponentDescriptor ScheduleDescriptor { get; } = CreateSourceDescriptor<TimerScheduleTick>(
        TimersComponentTypes.Schedule,
        CreateTimerScheduleNode);
    internal static ComponentDescriptor DelayDescriptor { get; } = CreateTransformDescriptor(
        TimersComponentTypes.Delay,
        CreateTimerDelayNode);
    internal static ComponentDescriptor ThrottleDescriptor { get; } = CreateTransformDescriptor(
        TimersComponentTypes.Throttle,
        CreateTimerThrottleNode);
    internal static ComponentDescriptor DebounceDescriptor { get; } = CreateTransformDescriptor(
        TimersComponentTypes.Debounce,
        CreateTimerDebounceNode);

    public static IServiceCollection AddTimersComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(IntervalDescriptor);
        services.AddFluxFlowComponent(ScheduleDescriptor);
        services.AddFluxFlowComponent(DelayDescriptor);
        services.AddFluxFlowComponent(ThrottleDescriptor);
        services.AddFluxFlowComponent(DebounceDescriptor);
        services.AddComponentDesignMetadataProvider<TimersComponentDesignMetadataProvider>();
        return services;
    }

    private static ComponentDescriptor CreateSourceDescriptor<TOutput>(
        string type,
        ComponentFactory factory)
        => new(
            type,
            factory,
            outputs: [ComponentPorts.Metadata<TOutput>(TimersComponentPortNames.Output)]);

    private static ComponentDescriptor CreateTransformDescriptor(
        string type,
        ComponentFactory factory)
        => new(
            type,
            factory,
            inputs: [ComponentPorts.Metadata<JsonElement>(TimersComponentPortNames.Input)],
            outputs: [ComponentPorts.Metadata<JsonElement>(TimersComponentPortNames.Output)]);

    private static ValueTask<ComponentInstance> CreateTimerIntervalNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerIntervalSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentResourceNames.Clock);
        var node = new TimerIntervalNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<TimerIntervalTick>(
                    TimersComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerScheduleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerScheduleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentResourceNames.Clock);
        var node = new TimerScheduleNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<TimerScheduleTick>(
                    TimersComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerDelayNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDelaySettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentResourceNames.Clock);
        var node = new TimerDelayNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    TimersComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    TimersComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerThrottleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerThrottleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentResourceNames.Clock);
        var node = new TimerThrottleNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    TimersComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    TimersComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerDebounceNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDebounceSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentResourceNames.Clock);
        var node = new TimerDebounceNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    TimersComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    TimersComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
