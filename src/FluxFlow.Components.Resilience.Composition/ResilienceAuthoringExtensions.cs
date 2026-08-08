using System.Text.Json;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Composition;

public static class ResilienceComponents
{
    public static ComponentContract<FlowRetryComponentBuilder, FlowRetryComponentHandle> FlowRetry { get; } =
        DesignedComponentContract.Create(
            ResilienceComponentDefinition.Types.Retry,
            ResilienceServiceCollectionExtensions.ConfigureRetry,
            static () => new FlowRetryComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new FlowRetryComponentHandle(component));
}

public static class ResilienceAuthoringExtensions
{
    public static FlowRetryComponentHandle AddFlowRetry(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FlowRetryComponentBuilder> configure)
        => workflow.AddComponent(name, ResilienceComponents.FlowRetry, configure);

    public static WorkflowDefinitionBuilder AddFlowRetry(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FlowRetryComponentBuilder> configure,
        out FlowRetryComponentHandle retry)
    {
        retry = workflow.AddFlowRetry(name, configure);
        return workflow;
    }
}

public sealed class FlowRetryComponentBuilder
{
    public string? Name { get; set; }
    public RetryBackoffStrategy? Strategy { get; set; }
    public int? InitialDelayMilliseconds { get; set; }
    public int? IncrementMilliseconds { get; set; }
    public int? MaximumDelayMilliseconds { get; set; }
    public int? MaximumAttempts { get; set; }
    public int? MaximumDurationMilliseconds { get; set; }
    public double? JitterFactor { get; set; }
    public int? AttemptTimeoutMilliseconds { get; set; }
    public int? Capacity { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }
    public ResourceHandle<IRetryJitterSource>? Jitter { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        Set(definition, ResilienceComponentDefinition.Options.Name, Name);
        if (Strategy is not null)
            definition.Set(ResilienceComponentDefinition.Options.Strategy, Strategy.Value.ToString());
        Set(definition, ResilienceComponentDefinition.Options.InitialDelayMilliseconds, InitialDelayMilliseconds);
        Set(definition, ResilienceComponentDefinition.Options.IncrementMilliseconds, IncrementMilliseconds);
        Set(definition, ResilienceComponentDefinition.Options.MaximumDelayMilliseconds, MaximumDelayMilliseconds);
        Set(definition, ResilienceComponentDefinition.Options.MaximumAttempts, MaximumAttempts);
        Set(definition, ResilienceComponentDefinition.Options.MaximumDurationMilliseconds, MaximumDurationMilliseconds);
        Set(definition, ResilienceComponentDefinition.Options.JitterFactor, JitterFactor);
        Set(definition, ResilienceComponentDefinition.Options.AttemptTimeoutMilliseconds, AttemptTimeoutMilliseconds);
        Set(definition, ResilienceComponentDefinition.Options.Capacity, Capacity);
        if (Clock is not null)
            definition.UseResource(ResilienceComponentDefinition.Resources.Clock, Clock);
        if (Jitter is not null)
            definition.UseResource(ResilienceComponentDefinition.Resources.Jitter, Jitter);
    }

    private static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class FlowRetryComponentHandle : AuthoredComponentHandle
{
    internal FlowRetryComponentHandle(ComponentHandle definition) : base(definition)
    {
        Input = definition.Input<JsonElement>(ResilienceComponentDefinition.Ports.Input);
        Ack = definition.SignalInput(ResilienceComponentDefinition.Ports.Ack);
        Nak = definition.SignalInput(ResilienceComponentDefinition.Ports.Nak);
        Cancel = definition.SignalInput(ResilienceComponentDefinition.Ports.Cancel);
        Output = definition.Output<RetrySignal<JsonElement>>(ResilienceComponentDefinition.Ports.Output);
        Events = definition.Output<ComponentEvent>(ResilienceComponentDefinition.Ports.Events);
    }

    public InputPortHandle<JsonElement> Input { get; }
    public SignalInputPortHandle Ack { get; }
    public SignalInputPortHandle Nak { get; }
    public SignalInputPortHandle Cancel { get; }
    public OutputPortHandle<RetrySignal<JsonElement>> Output { get; }
    public OutputPortHandle<ComponentEvent> Events { get; }
}
