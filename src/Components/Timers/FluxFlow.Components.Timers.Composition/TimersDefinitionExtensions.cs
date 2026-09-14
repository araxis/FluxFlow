using System.Text.Json;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;

using FluxFlow.Data;

namespace FluxFlow.Components.Timers.Composition;

public static class TimersDefinitionExtensions
{
    public static OutputComponentHandle<FlowValue> AddIntervalTimer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<IntervalTimerComponentBuilder> configure)
        => workflow.AddComponent(name, TimersComponentDefinition.Types.Interval,
            TimersServiceCollectionExtensions.CreateTimerIntervalNode,
            definition =>
            {
                var options = new IntervalTimerComponentBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new OutputComponentHandle<FlowValue>(component, TimersComponentDefinition.Ports.Output, TimersComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddIntervalTimer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<IntervalTimerComponentBuilder> configure,
        out OutputComponentHandle<FlowValue> timer)
    {
        timer = workflow.AddIntervalTimer(name, configure);
        return workflow;
    }

    public static OutputComponentHandle<FlowValue> AddScheduleTimer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<ScheduleTimerComponentBuilder> configure)
        => workflow.AddComponent(name, TimersComponentDefinition.Types.Schedule,
            TimersServiceCollectionExtensions.CreateTimerScheduleNode,
            definition =>
            {
                var options = new ScheduleTimerComponentBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new OutputComponentHandle<FlowValue>(component, TimersComponentDefinition.Ports.Output, TimersComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddScheduleTimer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<ScheduleTimerComponentBuilder> configure,
        out OutputComponentHandle<FlowValue> timer)
    {
        timer = workflow.AddScheduleTimer(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<FlowValue, FlowValue> AddDelay(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DelayComponentBuilder> configure)
        => workflow.AddComponent(name, TimersComponentDefinition.Types.Delay,
            TimersServiceCollectionExtensions.CreateTimerDelayNode,
            definition =>
            {
                var options = new DelayComponentBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, TimersComponentDefinition.Ports.Input, TimersComponentDefinition.Ports.Output, TimersComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddDelay(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DelayComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> delay)
    {
        delay = workflow.AddDelay(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<FlowValue, FlowValue> AddThrottle(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<ThrottleComponentBuilder> configure)
        => workflow.AddComponent(name, TimersComponentDefinition.Types.Throttle,
            TimersServiceCollectionExtensions.CreateTimerThrottleNode,
            definition =>
            {
                var options = new ThrottleComponentBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, TimersComponentDefinition.Ports.Input, TimersComponentDefinition.Ports.Output, TimersComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddThrottle(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<ThrottleComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> throttle)
    {
        throttle = workflow.AddThrottle(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<FlowValue, FlowValue> AddDebounce(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DebounceComponentBuilder> configure)
        => workflow.AddComponent(name, TimersComponentDefinition.Types.Debounce,
            TimersServiceCollectionExtensions.CreateTimerDebounceNode,
            definition =>
            {
                var options = new DebounceComponentBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, TimersComponentDefinition.Ports.Input, TimersComponentDefinition.Ports.Output, TimersComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddDebounce(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DebounceComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> debounce)
    {
        debounce = workflow.AddDebounce(name, configure);
        return workflow;
    }
}

public abstract class TimerComponentBuilder
{
    public string? Name { get; set; }
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    private protected void ApplyCommon(ComponentDefinitionBuilder definition)
    {
        Set(definition, TimersComponentDefinition.Options.Name, Name);
        Set(definition, TimersComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        if (Clock is not null)
            definition.UseResource(TimersComponentDefinition.Resources.Clock, Clock);
    }

    private protected static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class IntervalTimerComponentBuilder : TimerComponentBuilder
{
    public TimeSpan? Interval { get; set; }
    public TimeSpan? InitialDelay { get; set; }
    public bool? EmitImmediately { get; set; }
    public long? MaxTicks { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (Interval is null)
            throw new InvalidOperationException("Interval timers require Interval.");
        ApplyCommon(definition);
        definition.Set(TimersComponentDefinition.Options.Interval, Interval.Value);
        Set(definition, TimersComponentDefinition.Options.InitialDelay, InitialDelay);
        Set(definition, TimersComponentDefinition.Options.EmitImmediately, EmitImmediately);
        Set(definition, TimersComponentDefinition.Options.MaxTicks, MaxTicks);
    }
}

public sealed class ScheduleTimerComponentBuilder : TimerComponentBuilder
{
    public string? Cron { get; set; }
    public long? MaxTicks { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (string.IsNullOrWhiteSpace(Cron))
            throw new InvalidOperationException("Schedule timers require Cron.");
        ApplyCommon(definition);
        definition.Set(TimersComponentDefinition.Options.Cron, Cron);
        Set(definition, TimersComponentDefinition.Options.MaxTicks, MaxTicks);
    }
}

public sealed class DelayComponentBuilder : TimerComponentBuilder
{
    public TimeSpan? Delay { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (Delay is null)
            throw new InvalidOperationException("Delay components require Delay.");
        ApplyCommon(definition);
        definition.Set(TimersComponentDefinition.Options.Delay, Delay.Value);
    }
}

public sealed class ThrottleComponentBuilder : TimerComponentBuilder
{
    public TimeSpan? Interval { get; set; }
    public bool? EmitFirstImmediately { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (Interval is null)
            throw new InvalidOperationException("Throttle components require Interval.");
        ApplyCommon(definition);
        definition.Set(TimersComponentDefinition.Options.Interval, Interval.Value);
        Set(definition, TimersComponentDefinition.Options.EmitFirstImmediately, EmitFirstImmediately);
    }
}

public sealed class DebounceComponentBuilder : TimerComponentBuilder
{
    public TimeSpan? QuietPeriod { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (QuietPeriod is null)
            throw new InvalidOperationException("Debounce components require QuietPeriod.");
        ApplyCommon(definition);
        definition.Set(TimersComponentDefinition.Options.QuietPeriod, QuietPeriod.Value);
    }
}
