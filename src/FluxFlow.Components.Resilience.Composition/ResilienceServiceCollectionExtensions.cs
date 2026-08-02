using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Nodes;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Composition;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Composition;

public static class ResilienceServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddResilience(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(ResilienceComponentDefinition.Types.Retry, ConfigureRetry);
    }

    private static void ConfigureRetry(ComponentRegistrationBuilder component)
    {
        var defaults = new FlowRetryOptions();
        component.UseFactory(CreateFlowRetryNode);
        component.WithDisplay("Flow Retry", "Resilience", "Coordinates acknowledged workflow attempts with retry, timeout, cancellation, and exhaustion results.", "refresh-cw", "retry", 460);
        component.AddInput<JsonElement>(ResilienceComponentDefinition.Ports.Input, "Input", "Messages", 0, "Value that begins a retry-controlled logical operation.", true);
        component.AddSignalInput(ResilienceComponentDefinition.Ports.Ack, "Ack", "Signals", 1, "Completes the matching attempt successfully.");
        component.AddSignalInput(ResilienceComponentDefinition.Ports.Nak, "Nak", "Signals", 2, "Fails the matching attempt and applies retry policy.");
        component.AddSignalInput(ResilienceComponentDefinition.Ports.Cancel, "Cancel", "Signals", 3, "Cancels the matching logical operation.");
        component.AddOutput<RetrySignal<JsonElement>>(ResilienceComponentDefinition.Ports.Output, "Output", "Results", 4, "Attempt, scheduled retry, completion, exhaustion, cancellation, or rejection result.", true);
        component.AddOption<string>(ResilienceComponentDefinition.Options.Name, OptionValueKind.Text, "Name", "Optional diagnostic name; composition defaults to the component address.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<RetryBackoffStrategy>(ResilienceComponentDefinition.Options.Strategy, OptionValueKind.Enum, "Strategy", "Delay strategy applied after NAK or timeout.", defaultValue: defaults.Strategy.ToString(), section: "Retry", importance: OptionDesignMetadataAttributeValues.Primary);
        foreach (var strategy in Enum.GetValues<RetryBackoffStrategy>())
            component.AddOptionChoice(ResilienceComponentDefinition.Options.Strategy, strategy.ToString(), strategy.ToString());
        AddNumber<int>(component, ResilienceComponentDefinition.Options.InitialDelayMilliseconds, "Initial Delay Milliseconds", "Delay before the next attempt after NAK or timeout.", defaults.InitialDelayMilliseconds, "Timing", OptionDesignMetadataAttributeValues.Primary, 0);
        AddNumber<int>(component, ResilienceComponentDefinition.Options.IncrementMilliseconds, "Increment Milliseconds", "Amount added per retry when the Linear strategy is selected.", defaults.IncrementMilliseconds, "Timing", OptionDesignMetadataAttributeValues.Advanced, 0);
        AddNumber<int>(component, ResilienceComponentDefinition.Options.MaximumDelayMilliseconds, "Maximum Delay Milliseconds", "Upper bound for a calculated retry delay, including jitter.", defaults.MaximumDelayMilliseconds, "Timing", OptionDesignMetadataAttributeValues.Advanced, 0);
        AddNumber<int?>(component, ResilienceComponentDefinition.Options.MaximumAttempts, "Maximum Attempts", "Maximum total attempts for one logical operation.", defaults.MaximumAttempts, "Limits", OptionDesignMetadataAttributeValues.Primary, 1);
        AddNumber<int?>(component, ResilienceComponentDefinition.Options.MaximumDurationMilliseconds, "Maximum Duration Milliseconds", "Optional elapsed-time budget for all attempts and waits.", defaults.MaximumDurationMilliseconds, "Limits", OptionDesignMetadataAttributeValues.Advanced, 1);
        component.AddOption<double>(ResilienceComponentDefinition.Options.JitterFactor, OptionValueKind.Number, "Jitter Factor", "Random delay variation from zero through one.", defaultValue: defaults.JitterFactor, min: 0, max: 1, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        AddNumber<int>(component, ResilienceComponentDefinition.Options.AttemptTimeoutMilliseconds, "Attempt Timeout Milliseconds", "Maximum time to wait for Ack, Nak, or Cancel for one attempt.", defaults.AttemptTimeoutMilliseconds, "Timeouts", OptionDesignMetadataAttributeValues.Primary, 1);
        AddNumber<int>(component, ResilienceComponentDefinition.Options.Capacity, "Capacity", "Capacity shared by queued inputs, logical retry operations, pending feedback, and reliable normal-data result output.", defaults.Capacity, "Runtime", OptionDesignMetadataAttributeValues.Advanced, 1);
        component.AddResource<TimeProvider>(ResilienceComponentDefinition.Resources.Clock, "Clock", 0, "Optional host-owned clock for deterministic attempt timeouts and retry delays.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        component.AddResource<IRetryJitterSource>(ResilienceComponentDefinition.Resources.Jitter, "Jitter", 1, "Optional host-owned jitter sample source for deterministic retry timing.", designValueType: nameof(IRetryJitterSource), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Delegate, keyPattern: "Resources.{name}");
    }

    private static void AddNumber<T>(ComponentRegistrationBuilder component, string name, string displayName, string helperText, object? defaultValue, string section, string importance, double min)
        => component.AddOption<T>(name, OptionValueKind.Number, displayName, helperText, defaultValue: defaultValue, min: min, section: section, importance: importance, editor: OptionDesignMetadataAttributeValues.Number);

    private static ValueTask<ComponentInstance> CreateFlowRetryNode(
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

        var node = new FlowRetryNode(
            options,
            context.GetResource<TimeProvider>(ResilienceComponentDefinition.Resources.Clock),
            context.GetResource<IRetryJitterSource>(ResilienceComponentDefinition.Resources.Jitter));
        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ResilienceComponentDefinition.Ports.Input,
                    node.Input),
                ComponentPorts.SignalInput(
                    ResilienceComponentDefinition.Ports.Ack,
                    node.Ack),
                ComponentPorts.SignalInput(
                    ResilienceComponentDefinition.Ports.Nak,
                    node.Nak),
                ComponentPorts.SignalInput(
                    ResilienceComponentDefinition.Ports.Cancel,
                    node.Cancel)
            ],
            outputs:
            [
                ComponentPorts.Output<RetrySignal<JsonElement>>(
                    ResilienceComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
