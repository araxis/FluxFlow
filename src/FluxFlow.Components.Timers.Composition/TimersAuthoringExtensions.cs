using System.Text.Json;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Timers.Composition;

public static class TimersAuthoringExtensions
{
    public static OutputComponentHandle<TimerIntervalTick> AddIntervalTimer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<IntervalTimerComponentBuilder> configure)
        => AddSource<TimerIntervalTick, IntervalTimerComponentBuilder>(
            workflow, name, TimersComponentDefinition.Types.Interval, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddIntervalTimer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<IntervalTimerComponentBuilder> configure,
        out OutputComponentHandle<TimerIntervalTick> timer)
    {
        timer = workflow.AddIntervalTimer(name, configure);
        return workflow;
    }

    public static OutputComponentHandle<TimerScheduleTick> AddScheduleTimer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<ScheduleTimerComponentBuilder> configure)
        => AddSource<TimerScheduleTick, ScheduleTimerComponentBuilder>(
            workflow, name, TimersComponentDefinition.Types.Schedule, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddScheduleTimer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<ScheduleTimerComponentBuilder> configure,
        out OutputComponentHandle<TimerScheduleTick> timer)
    {
        timer = workflow.AddScheduleTimer(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<JsonElement, JsonElement> AddDelay(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DelayComponentBuilder> configure)
        => AddTransform<DelayComponentBuilder>(
            workflow, name, TimersComponentDefinition.Types.Delay, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddDelay(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DelayComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, JsonElement> delay)
    {
        delay = workflow.AddDelay(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<JsonElement, JsonElement> AddThrottle(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<ThrottleComponentBuilder> configure)
        => AddTransform<ThrottleComponentBuilder>(
            workflow, name, TimersComponentDefinition.Types.Throttle, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddThrottle(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<ThrottleComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, JsonElement> throttle)
    {
        throttle = workflow.AddThrottle(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<JsonElement, JsonElement> AddDebounce(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DebounceComponentBuilder> configure)
        => AddTransform<DebounceComponentBuilder>(
            workflow, name, TimersComponentDefinition.Types.Debounce, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddDebounce(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DebounceComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, JsonElement> debounce)
    {
        debounce = workflow.AddDebounce(name, configure);
        return workflow;
    }

    private static OutputComponentHandle<TOutput> AddSource<TOutput, TBuilder>(
        WorkflowDefinitionBuilder workflow,
        string name,
        string type,
        Action<TBuilder> configure,
        Action<TBuilder, ComponentDefinitionBuilder> apply)
        where TBuilder : TimerComponentBuilder, new()
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var component = workflow.AddComponent(name, type, definition =>
        {
            var builder = new TBuilder();
            configure(builder);
            apply(builder, definition);
        });
        return new(component, TimersComponentDefinition.Ports.Output);
    }

    private static InputOutputComponentHandle<JsonElement, JsonElement> AddTransform<TBuilder>(
        WorkflowDefinitionBuilder workflow,
        string name,
        string type,
        Action<TBuilder> configure,
        Action<TBuilder, ComponentDefinitionBuilder> apply)
        where TBuilder : TimerComponentBuilder, new()
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var component = workflow.AddComponent(name, type, definition =>
        {
            var builder = new TBuilder();
            configure(builder);
            apply(builder, definition);
        });
        return new(component, TimersComponentDefinition.Ports.Input, TimersComponentDefinition.Ports.Output);
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
