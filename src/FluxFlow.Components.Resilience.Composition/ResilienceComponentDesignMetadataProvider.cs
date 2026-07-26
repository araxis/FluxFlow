using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Composition;

public sealed class ResilienceComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly FlowRetryOptions Defaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateRetryMetadata()];

    private static ComponentDesignMetadata CreateRetryMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(ResilienceCompositionNodeTypes.Retry)
            .WithDisplay(
                displayName: "Flow Retry",
                category: "Resilience",
                summary: "Coordinates acknowledged workflow attempts with retry, timeout, cancellation, and exhaustion results.",
                iconKey: "refresh-cw",
                preferredNodeName: "retry",
                suggestedEditorWidth: 460);

        AddOptions(builder);
        AddResources(builder);
        AddPorts(builder);
        return builder.Build();
    }

    private static void AddOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                "name",
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional diagnostic name; composition defaults to the component address.",
                attributes: Hints("Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "strategy",
                OptionValueKind.Enum,
                displayName: "Strategy",
                helperText: "Delay strategy applied after NAK or timeout.",
                defaultValue: Defaults.Strategy.ToString(),
                choices:
                [
                    Choice(RetryBackoffStrategy.Fixed),
                    Choice(RetryBackoffStrategy.Linear),
                    Choice(RetryBackoffStrategy.Exponential)
                ],
                attributes: Hints("Retry", OptionDesignMetadataAttributeValues.Primary))
            .AddOption(Number(
                "initialDelayMilliseconds",
                "Initial Delay Milliseconds",
                "Delay before the next attempt after NAK or timeout.",
                Defaults.InitialDelayMilliseconds,
                "Timing",
                OptionDesignMetadataAttributeValues.Primary,
                min: 0))
            .AddOption(Number(
                "incrementMilliseconds",
                "Increment Milliseconds",
                "Amount added per retry when the Linear strategy is selected.",
                Defaults.IncrementMilliseconds,
                "Timing",
                OptionDesignMetadataAttributeValues.Advanced,
                min: 0))
            .AddOption(Number(
                "maximumDelayMilliseconds",
                "Maximum Delay Milliseconds",
                "Upper bound for a calculated retry delay, including jitter.",
                Defaults.MaximumDelayMilliseconds,
                "Timing",
                OptionDesignMetadataAttributeValues.Advanced,
                min: 0))
            .AddOption(Number(
                "maximumAttempts",
                "Maximum Attempts",
                "Maximum total attempts for one logical operation.",
                Defaults.MaximumAttempts,
                "Limits",
                OptionDesignMetadataAttributeValues.Primary,
                min: 1))
            .AddOption(Number(
                "maximumDurationMilliseconds",
                "Maximum Duration Milliseconds",
                "Optional elapsed-time budget for all attempts and waits.",
                Defaults.MaximumDurationMilliseconds,
                "Limits",
                OptionDesignMetadataAttributeValues.Advanced,
                min: 1))
            .AddOption(
                "jitterFactor",
                OptionValueKind.Number,
                displayName: "Jitter Factor",
                helperText: "Random delay variation from zero through one.",
                defaultValue: Defaults.JitterFactor,
                min: 0,
                max: 1,
                attributes: Hints("Timing", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number))
            .AddOption(Number(
                "attemptTimeoutMilliseconds",
                "Attempt Timeout Milliseconds",
                "Maximum time to wait for Ack, Nak, or Cancel for one attempt.",
                Defaults.AttemptTimeoutMilliseconds,
                "Timeouts",
                OptionDesignMetadataAttributeValues.Primary,
                min: 1))
            .AddOption(Number(
                "capacity",
                "Capacity",
                "Maximum concurrent logical retry operations accepted by this component.",
                Defaults.Capacity,
                "Runtime",
                OptionDesignMetadataAttributeValues.Advanced,
                min: 1));

    private static void AddResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                ResilienceCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional host-owned clock for deterministic attempt timeouts and retry delays.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                ResilienceCompositionResourceNames.Jitter,
                displayName: "Jitter",
                order: 1,
                summary: "Optional host-owned jitter sample source for deterministic retry timing.",
                valueType: nameof(IRetryJitterSource),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Delegate,
                    keyPattern: "Resources.{name}"));

    private static void AddPorts(ComponentDesignMetadataBuilder builder)
    {
        builder.AddInputPort(
            ResilienceCompositionPortNames.Input,
            displayName: "Input",
            group: "Messages",
            order: 0,
            summary: "Value that begins a retry-controlled logical operation.",
            valueType: nameof(JsonElement),
            isPrimary: true);
        AddSignal(builder, ResilienceCompositionPortNames.Ack, 1, "Completes the matching attempt successfully.");
        AddSignal(builder, ResilienceCompositionPortNames.Nak, 2, "Fails the matching attempt and applies retry policy.");
        AddSignal(builder, ResilienceCompositionPortNames.Cancel, 3, "Cancels the matching logical operation.");
        builder.AddOutputPort(
            ResilienceCompositionPortNames.Output,
            displayName: "Output",
            group: "Results",
            order: 4,
            summary: "Attempt, scheduled retry, completion, exhaustion, cancellation, or rejection result.",
            valueType: "RetrySignal<JsonElement>",
            isPrimary: true);
    }

    private static void AddSignal(
        ComponentDesignMetadataBuilder builder,
        string name,
        int order,
        string summary)
        => builder.AddInputPort(
            name,
            displayName: name,
            group: "Signals",
            order: order,
            summary: summary,
            valueType: nameof(Object),
            attributes: PortDesignMetadataAttributes.CreateSignal());

    private static OptionDesignMetadata Number(
        string name,
        string displayName,
        string helperText,
        object? defaultValue,
        string section,
        string importance,
        double min)
        => new()
        {
            Name = new ComponentOptionName(name),
            Kind = OptionValueKind.Number,
            DisplayName = new ComponentMetadataText(displayName),
            HelperText = new ComponentMetadataText(helperText),
            DefaultValue = defaultValue,
            Min = min,
            Attributes = OptionDesignMetadataAttributes.CreateMap(
                section,
                importance,
                OptionDesignMetadataAttributeValues.Number)
        };

    private static IReadOnlyDictionary<string, string> Hints(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

    private static OptionChoiceMetadata Choice(RetryBackoffStrategy strategy)
        => new()
        {
            Value = new ComponentOptionChoiceValue(strategy.ToString()),
            DisplayName = new ComponentMetadataText(strategy.ToString())
        };
}
