using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Composition;

public static class TimersServiceCollectionExtensions
{
    public static IServiceCollection AddTimers(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(TimersComponentDefinition.Types.Interval, TimersServiceCollectionExtensions.CreateTimerIntervalNode)
            .AddComponent(TimersComponentDefinition.Types.Schedule, TimersServiceCollectionExtensions.CreateTimerScheduleNode)
            .AddComponent(TimersComponentDefinition.Types.Delay, TimersServiceCollectionExtensions.CreateTimerDelayNode)
            .AddComponent(TimersComponentDefinition.Types.Throttle, TimersServiceCollectionExtensions.CreateTimerThrottleNode)
            .AddComponent(TimersComponentDefinition.Types.Debounce, TimersServiceCollectionExtensions.CreateTimerDebounceNode);
    }

    internal static ValueTask<IFlowNode> CreateTimerIntervalNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerIntervalSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerIntervalNode(settings, clock);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateTimerScheduleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerScheduleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerScheduleNode(settings, clock);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateTimerDelayNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDelaySettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerDelayNode(settings, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateTimerThrottleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerThrottleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerThrottleNode(settings, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateTimerDebounceNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDebounceSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        var node = new TimerDebounceNode(settings, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
