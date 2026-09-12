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
    public static FluxFlowRegistrationBuilder AddTimers(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(TimersComponents.IntervalTimer)
            .AddComponent(TimersComponents.ScheduleTimer)
            .AddComponent(TimersComponents.Delay)
            .AddComponent(TimersComponents.Throttle)
            .AddComponent(TimersComponents.Debounce);
    }

    internal static void ConfigureInterval(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(TimersComponentDefinition.Resources.Clock);
        component.AddOption<string>(TimersComponentDefinition.Options.Name);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.Interval, isRequired: true);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.InitialDelay);
        component.AddOption<bool>(TimersComponentDefinition.Options.EmitImmediately);
        component.AddOption<long?>(TimersComponentDefinition.Options.MaxTicks);
        component.AddOption<int>(TimersComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateTimerIntervalNode)
            .HasOutput(TimersComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(TimersComponentDefinition.Ports.Events, static node => node.Events);
    }

    internal static void ConfigureSchedule(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(TimersComponentDefinition.Resources.Clock);
        component.AddOption<string>(TimersComponentDefinition.Options.Name);
        component.AddOption<string>(TimersComponentDefinition.Options.Cron, isRequired: true);
        component.AddOption<long?>(TimersComponentDefinition.Options.MaxTicks);
        component.AddOption<int>(TimersComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateTimerScheduleNode)
            .HasOutput(TimersComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(TimersComponentDefinition.Ports.Events, static node => node.Events);
    }

    internal static void ConfigureDelay(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(TimersComponentDefinition.Resources.Clock);
        component.AddOption<string>(TimersComponentDefinition.Options.Name);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.Delay, isRequired: true);
        component.AddOption<int>(TimersComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateTimerDelayNode)
            .HasStandardPorts(FlowValueShape.Any("Any canonical workflow value."));
    }

    internal static void ConfigureThrottle(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(TimersComponentDefinition.Resources.Clock);
        component.AddOption<string>(TimersComponentDefinition.Options.Name);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.Interval, isRequired: true);
        component.AddOption<bool>(TimersComponentDefinition.Options.EmitFirstImmediately);
        component.AddOption<int>(TimersComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateTimerThrottleNode)
            .HasStandardPorts(FlowValueShape.Any("Any canonical workflow value."));
    }

    internal static void ConfigureDebounce(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(TimersComponentDefinition.Resources.Clock);
        component.AddOption<string>(TimersComponentDefinition.Options.Name);
        component.AddOption<TimeSpan>(TimersComponentDefinition.Options.QuietPeriod, isRequired: true);
        component.AddOption<int>(TimersComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateTimerDebounceNode)
            .HasStandardPorts(FlowValueShape.Any("Any canonical workflow value."));
    }

    private static TimerIntervalNode CreateTimerIntervalNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerIntervalSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        return new TimerIntervalNode(settings, clock);
    }

    private static TimerScheduleNode CreateTimerScheduleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerScheduleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        return new TimerScheduleNode(settings, clock);
    }

    private static TimerDelayNode CreateTimerDelayNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDelaySettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        return new TimerDelayNode(settings, clock);
    }

    private static TimerThrottleNode CreateTimerThrottleNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerThrottleSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        return new TimerThrottleNode(settings, clock);
    }

    private static TimerDebounceNode CreateTimerDebounceNode(
        ComponentActivationContext context)
    {
        var settings = context.BindConfiguration<TimerDebounceSettings>();
        var clock = context.GetResource<TimeProvider>(
            TimersComponentDefinition.Resources.Clock);
        return new TimerDebounceNode(settings, clock);
    }
}
