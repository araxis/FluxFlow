using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Composition;

public static partial class ResilienceComponentDefinition
{
    private static readonly FlowRetryOptions Defaults = new();

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        => [CreateRetryMetadata()];

    private static ComponentDesignMetadata CreateRetryMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(ResilienceComponentDefinition.Types.Retry)
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
                Options.Name,
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional diagnostic name; composition defaults to the component address.",
                attributes: Hints("Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.Strategy,
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
                Options.InitialDelayMilliseconds,
                "Initial Delay Milliseconds",
                "Delay before the next attempt after NAK or timeout.",
                Defaults.InitialDelayMilliseconds,
                "Timing",
                OptionDesignMetadataAttributeValues.Primary,
                min: 0))
            .AddOption(Number(
                Options.IncrementMilliseconds,
                "Increment Milliseconds",
                "Amount added per retry when the Linear strategy is selected.",
                Defaults.IncrementMilliseconds,
                "Timing",
                OptionDesignMetadataAttributeValues.Advanced,
                min: 0))
            .AddOption(Number(
                Options.MaximumDelayMilliseconds,
                "Maximum Delay Milliseconds",
                "Upper bound for a calculated retry delay, including jitter.",
                Defaults.MaximumDelayMilliseconds,
                "Timing",
                OptionDesignMetadataAttributeValues.Advanced,
                min: 0))
            .AddOption(Number(
                Options.MaximumAttempts,
                "Maximum Attempts",
                "Maximum total attempts for one logical operation.",
                Defaults.MaximumAttempts,
                "Limits",
                OptionDesignMetadataAttributeValues.Primary,
                min: 1))
            .AddOption(Number(
                Options.MaximumDurationMilliseconds,
                "Maximum Duration Milliseconds",
                "Optional elapsed-time budget for all attempts and waits.",
                Defaults.MaximumDurationMilliseconds,
                "Limits",
                OptionDesignMetadataAttributeValues.Advanced,
                min: 1))
            .AddOption(
                Options.JitterFactor,
                OptionValueKind.Number,
                displayName: "Jitter Factor",
                helperText: "Random delay variation from zero through one.",
                defaultValue: Defaults.JitterFactor,
                min: 0,
                max: 1,
                attributes: Hints("Timing", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number))
            .AddOption(Number(
                Options.AttemptTimeoutMilliseconds,
                "Attempt Timeout Milliseconds",
                "Maximum time to wait for Ack, Nak, or Cancel for one attempt.",
                Defaults.AttemptTimeoutMilliseconds,
                "Timeouts",
                OptionDesignMetadataAttributeValues.Primary,
                min: 1))
            .AddOption(Number(
                Options.Capacity,
                "Capacity",
                "Maximum concurrent logical retry operations accepted by this component.",
                Defaults.Capacity,
                "Runtime",
                OptionDesignMetadataAttributeValues.Advanced,
                min: 1));

    private static void AddResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                ResilienceComponentDefinition.Resources.Clock,
                displayName: Resources.Clock,
                order: 0,
                summary: "Optional host-owned clock for deterministic attempt timeouts and retry delays.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                ResilienceComponentDefinition.Resources.Jitter,
                displayName: Resources.Jitter,
                order: 1,
                summary: "Optional host-owned jitter sample source for deterministic retry timing.",
                valueType: nameof(IRetryJitterSource),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Delegate,
                    keyPattern: "Resources.{name}"));

    private static void AddPorts(ComponentDesignMetadataBuilder builder)
    {
        builder.AddInputPort(
            ResilienceComponentDefinition.Ports.Input,
            displayName: Ports.Input,
            group: "Messages",
            order: 0,
            summary: "Value that begins a retry-controlled logical operation.",
            valueType: nameof(JsonElement),
            isPrimary: true);
        AddSignal(builder, ResilienceComponentDefinition.Ports.Ack, 1, "Completes the matching attempt successfully.");
        AddSignal(builder, ResilienceComponentDefinition.Ports.Nak, 2, "Fails the matching attempt and applies retry policy.");
        AddSignal(builder, ResilienceComponentDefinition.Ports.Cancel, 3, "Cancels the matching logical operation.");
        builder.AddOutputPort(
            ResilienceComponentDefinition.Ports.Output,
            displayName: Ports.Output,
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


    public static class Options
    {
        public const string Name = "name";
        public const string Strategy = "strategy";
        public const string InitialDelayMilliseconds = "initialDelayMilliseconds";
        public const string IncrementMilliseconds = "incrementMilliseconds";
        public const string MaximumDelayMilliseconds = "maximumDelayMilliseconds";
        public const string MaximumAttempts = "maximumAttempts";
        public const string MaximumDurationMilliseconds = "maximumDurationMilliseconds";
        public const string JitterFactor = "jitterFactor";
        public const string AttemptTimeoutMilliseconds = "attemptTimeoutMilliseconds";
        public const string Capacity = "capacity";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Retry =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<RetryBackoffStrategy>(Options.Strategy),
                ComponentOptions.Metadata<int>(Options.InitialDelayMilliseconds),
                ComponentOptions.Metadata<int>(Options.IncrementMilliseconds),
                ComponentOptions.Metadata<int>(Options.MaximumDelayMilliseconds),
                ComponentOptions.Metadata<int?>(Options.MaximumAttempts),
                ComponentOptions.Metadata<int?>(Options.MaximumDurationMilliseconds),
                ComponentOptions.Metadata<double>(Options.JitterFactor),
                ComponentOptions.Metadata<int>(Options.AttemptTimeoutMilliseconds),
                ComponentOptions.Metadata<int>(Options.Capacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Retry =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock),
                ComponentResources.Metadata<IRetryJitterSource>(Resources.Jitter)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Retry = "flow.retry";
    }

    public static class Ports
    {
        public const string Input = "Input";
        public const string Ack = "Ack";
        public const string Nak = "Nak";
        public const string Cancel = "Cancel";
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Clock = "Clock";
        public const string Jitter = "Jitter";
    }
}
