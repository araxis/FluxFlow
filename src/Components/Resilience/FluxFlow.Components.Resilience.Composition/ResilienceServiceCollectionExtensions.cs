using FluxFlow.Components.Resilience.Nodes;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Composition;

public static class ResilienceServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddResilience(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(ResilienceComponents.FlowRetry);
    }

    internal static void ConfigureRetry(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(ResilienceComponentDefinition.Options.Name);
        component.AddOption<RetryBackoffStrategy>(ResilienceComponentDefinition.Options.Strategy);
        component.AddOption<int>(ResilienceComponentDefinition.Options.InitialDelayMilliseconds);
        component.AddOption<int>(ResilienceComponentDefinition.Options.IncrementMilliseconds);
        component.AddOption<int>(ResilienceComponentDefinition.Options.MaximumDelayMilliseconds);
        component.AddOption<int?>(ResilienceComponentDefinition.Options.MaximumAttempts);
        component.AddOption<int?>(ResilienceComponentDefinition.Options.MaximumDurationMilliseconds);
        component.AddOption<double>(ResilienceComponentDefinition.Options.JitterFactor);
        component.AddOption<int>(ResilienceComponentDefinition.Options.AttemptTimeoutMilliseconds);
        component.AddOption<int>(ResilienceComponentDefinition.Options.Capacity);
        component.AddResource<TimeProvider>(ResilienceComponentDefinition.Resources.Clock);
        component.AddResource<IRetryJitterSource>(ResilienceComponentDefinition.Resources.Jitter);
        var feedbackShape = FlowValueShape.Any("Payload-independent feedback matched by trace and attempt headers.");
        component
            .UseFactory(CreateFlowRetryNode)
            .HasInput(ResilienceComponentDefinition.Ports.Input, static node => node.Input, FlowValueShape.Any("Any workflow value that begins a retry-controlled logical operation."))
            .HasInput(ResilienceComponentDefinition.Ports.Ack, static node => node.Ack, feedbackShape)
            .HasInput(ResilienceComponentDefinition.Ports.Nak, static node => node.Nak, feedbackShape)
            .HasInput(ResilienceComponentDefinition.Ports.Cancel, static node => node.Cancel, feedbackShape)
            .HasOutput(ResilienceComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(ResilienceComponentDefinition.Ports.Events, static node => node.Events);
    }

    private static FlowRetryNode CreateFlowRetryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowRetryOptions>();
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            options = options with
            {
                Name = $"{context.WorkflowName}.{context.ComponentName}"
            };
        }

        return new FlowRetryNode(
            options,
            context.GetResource<TimeProvider>(ResilienceComponentDefinition.Resources.Clock),
            context.GetResource<IRetryJitterSource>(ResilienceComponentDefinition.Resources.Jitter));
    }
}
