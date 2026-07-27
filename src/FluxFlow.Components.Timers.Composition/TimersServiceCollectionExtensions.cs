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
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                IntervalDescriptor,
                ScheduleDescriptor,
                DelayDescriptor,
                ThrottleDescriptor,
                DebounceDescriptor
            ],
            TimersComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor IntervalDescriptor { get; } = CreateSourceDescriptor<TimerIntervalTick>(
        TimersComponentDefinition.Types.Interval,
        CreateTimerIntervalNode);
    internal static ComponentDescriptor ScheduleDescriptor { get; } = CreateSourceDescriptor<TimerScheduleTick>(
        TimersComponentDefinition.Types.Schedule,
        CreateTimerScheduleNode);
    internal static ComponentDescriptor DelayDescriptor { get; } = CreateTransformDescriptor(
        TimersComponentDefinition.Types.Delay,
        CreateTimerDelayNode);
    internal static ComponentDescriptor ThrottleDescriptor { get; } = CreateTransformDescriptor(
        TimersComponentDefinition.Types.Throttle,
        CreateTimerThrottleNode);
    internal static ComponentDescriptor DebounceDescriptor { get; } = CreateTransformDescriptor(
        TimersComponentDefinition.Types.Debounce,
        CreateTimerDebounceNode);

    public static IServiceCollection AddTimersComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ComponentDescriptor CreateSourceDescriptor<TOutput>(
        string type,
        ComponentFactory factory)
        => new(
            type,
            factory,
            outputs: [ComponentPorts.Metadata<TOutput>(TimersComponentDefinition.Ports.Output)],
            options: TimersComponentDefinition.CreateOptions(type),
            resources: TimersComponentDefinition.CreateResources(type));

    private static ComponentDescriptor CreateTransformDescriptor(
        string type,
        ComponentFactory factory)
        => new(
            type,
            factory,
            inputs: [ComponentPorts.Metadata<JsonElement>(TimersComponentDefinition.Ports.Input)],
            outputs: [ComponentPorts.Metadata<JsonElement>(TimersComponentDefinition.Ports.Output)],
            options: TimersComponentDefinition.CreateOptions(type),
            resources: TimersComponentDefinition.CreateResources(type));

    private static ValueTask<ComponentInstance> CreateTimerIntervalNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerIntervalSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerIntervalNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<TimerIntervalTick>(
                    TimersComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerScheduleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerScheduleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerScheduleNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<TimerScheduleTick>(
                    TimersComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerDelayNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDelaySettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerDelayNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    TimersComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    TimersComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerThrottleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerThrottleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerThrottleNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    TimersComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    TimersComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateTimerDebounceNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDebounceSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerDebounceNode(settings, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    TimersComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    TimersComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
