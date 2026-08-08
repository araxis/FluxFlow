using System.Text.Json;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Timers.Composition;

public static class TimersComponents
{
    public static ComponentContract<IntervalTimerComponentBuilder, OutputComponentHandle<TimerIntervalTick>> IntervalTimer { get; } =
        CreateSource<IntervalTimerComponentBuilder, TimerIntervalTick>(TimersComponentDefinition.Types.Interval, TimersServiceCollectionExtensions.ConfigureInterval, static (options, definition) => options.Apply(definition));

    public static ComponentContract<ScheduleTimerComponentBuilder, OutputComponentHandle<TimerScheduleTick>> ScheduleTimer { get; } =
        CreateSource<ScheduleTimerComponentBuilder, TimerScheduleTick>(TimersComponentDefinition.Types.Schedule, TimersServiceCollectionExtensions.ConfigureSchedule, static (options, definition) => options.Apply(definition));

    public static ComponentContract<DelayComponentBuilder, InputOutputComponentHandle<JsonElement, JsonElement>> Delay { get; } =
        CreateTransform<DelayComponentBuilder>(TimersComponentDefinition.Types.Delay, TimersServiceCollectionExtensions.ConfigureDelay, static (options, definition) => options.Apply(definition));

    public static ComponentContract<ThrottleComponentBuilder, InputOutputComponentHandle<JsonElement, JsonElement>> Throttle { get; } =
        CreateTransform<ThrottleComponentBuilder>(TimersComponentDefinition.Types.Throttle, TimersServiceCollectionExtensions.ConfigureThrottle, static (options, definition) => options.Apply(definition));

    public static ComponentContract<DebounceComponentBuilder, InputOutputComponentHandle<JsonElement, JsonElement>> Debounce { get; } =
        CreateTransform<DebounceComponentBuilder>(TimersComponentDefinition.Types.Debounce, TimersServiceCollectionExtensions.ConfigureDebounce, static (options, definition) => options.Apply(definition));

    private static ComponentContract<TOptions, OutputComponentHandle<TOutput>> CreateSource<TOptions, TOutput>(
        string type,
        Action<ComponentRegistrationBuilder> configure,
        Action<TOptions, ComponentDefinitionBuilder> apply)
        where TOptions : class, new()
        => DesignedComponentContract.Create(
            type,
            configure,
            static () => new TOptions(),
            apply,
            static component => new OutputComponentHandle<TOutput>(component, TimersComponentDefinition.Ports.Output, TimersComponentDefinition.Ports.Events));

    private static ComponentContract<TOptions, InputOutputComponentHandle<JsonElement, JsonElement>> CreateTransform<TOptions>(
        string type,
        Action<ComponentRegistrationBuilder> configure,
        Action<TOptions, ComponentDefinitionBuilder> apply)
        where TOptions : class, new()
        => DesignedComponentContract.Create(
            type,
            configure,
            static () => new TOptions(),
            apply,
            static component => new InputOutputComponentHandle<JsonElement, JsonElement>(component, TimersComponentDefinition.Ports.Input, TimersComponentDefinition.Ports.Output, TimersComponentDefinition.Ports.Events));
}

public static class TimersAuthoringExtensions
{
    public static OutputComponentHandle<TimerIntervalTick> AddIntervalTimer(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<IntervalTimerComponentBuilder> configure)
        => workflow.AddComponent(name, TimersComponents.IntervalTimer, configure);

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
        => workflow.AddComponent(name, TimersComponents.ScheduleTimer, configure);

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
        => workflow.AddComponent(name, TimersComponents.Delay, configure);

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
        => workflow.AddComponent(name, TimersComponents.Throttle, configure);

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
        => workflow.AddComponent(name, TimersComponents.Debounce, configure);

    public static WorkflowDefinitionBuilder AddDebounce(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DebounceComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, JsonElement> debounce)
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
