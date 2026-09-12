using FluxFlow.Resilience;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Composition;
using Definition = FluxFlow.Components.Resilience.Composition.ResilienceComponentDefinition;
using OptionHints = FluxFlow.Components.Designer.Contracts.OptionDesignMetadataAttributeValues;

namespace FluxFlow.Components.Resilience.Designer;

public static class ResilienceDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddResilienceDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignMetadata(Definition.Types.Retry, Describe);
    }

    private static ComponentDesignMetadata Describe(ComponentDesignMetadata metadata)
    {
        var defaults = new FlowRetryOptions();
        var options = new Dictionary<string, Func<OptionDesignMetadata, OptionDesignMetadata>>(StringComparer.Ordinal)
        {
            [Definition.Options.Name] = option => DescribeOption(option, OptionValueKind.Text,
                "Name", "Optional diagnostic name; composition defaults to the component address.", "Diagnostics",
                editor: OptionHints.Text),
            [Definition.Options.Strategy] = option => DescribeOption(option, OptionValueKind.Enum,
                "Strategy", "Delay strategy applied after NAK or timeout.", "Retry",
                defaultValue: defaults.Strategy.ToString(), importance: OptionHints.Primary,
                choices: Enum.GetValues<RetryBackoffStrategy>().Select(strategy => new OptionChoiceMetadata
                { Value = new(strategy.ToString()), DisplayName = new(strategy.ToString()) }).ToArray()),
            [Definition.Options.InitialDelayMilliseconds] = option => DescribeOption(option, OptionValueKind.Number,
                "Initial Delay Milliseconds", "Delay before the next attempt after NAK or timeout.", "Timing",
                defaultValue: defaults.InitialDelayMilliseconds, importance: OptionHints.Primary, min: 0, editor: OptionHints.Number),
            [Definition.Options.IncrementMilliseconds] = option => DescribeOption(option, OptionValueKind.Number,
                "Increment Milliseconds", "Amount added per retry when the Linear strategy is selected.", "Timing",
                defaultValue: defaults.IncrementMilliseconds, min: 0, editor: OptionHints.Number),
            [Definition.Options.MaximumDelayMilliseconds] = option => DescribeOption(option, OptionValueKind.Number,
                "Maximum Delay Milliseconds", "Upper bound for a calculated retry delay, including jitter.", "Timing",
                defaultValue: defaults.MaximumDelayMilliseconds, min: 0, editor: OptionHints.Number),
            [Definition.Options.MaximumAttempts] = option => DescribeOption(option, OptionValueKind.Number,
                "Maximum Attempts", "Maximum total attempts for one logical operation.", "Limits",
                defaultValue: defaults.MaximumAttempts, importance: OptionHints.Primary, min: 1, editor: OptionHints.Number),
            [Definition.Options.MaximumDurationMilliseconds] = option => DescribeOption(option, OptionValueKind.Number,
                "Maximum Duration Milliseconds", "Optional elapsed-time budget for all attempts and waits.", "Limits",
                defaultValue: defaults.MaximumDurationMilliseconds, min: 1, editor: OptionHints.Number),
            [Definition.Options.JitterFactor] = option => DescribeOption(option, OptionValueKind.Number,
                "Jitter Factor", "Random delay variation from zero through one.", "Timing",
                defaultValue: defaults.JitterFactor, min: 0, max: 1, editor: OptionHints.Number),
            [Definition.Options.AttemptTimeoutMilliseconds] = option => DescribeOption(option, OptionValueKind.Number,
                "Attempt Timeout Milliseconds", "Maximum time to wait for Ack, Nak, or Cancel for one attempt.", "Timeouts",
                defaultValue: defaults.AttemptTimeoutMilliseconds, importance: OptionHints.Primary, min: 1, editor: OptionHints.Number),
            [Definition.Options.Capacity] = option => DescribeOption(option, OptionValueKind.Number,
                "Capacity", "Capacity shared by queued inputs, logical retry operations, pending feedback, and reliable normal-data result output.", "Runtime",
                defaultValue: defaults.Capacity, min: 1, editor: OptionHints.Number)
        };
        var ports = new Dictionary<string, (string Group, int Order, string Summary, bool Primary)>(StringComparer.Ordinal)
        {
            [Definition.Ports.Input] = ("Messages", 0, "Value that begins a retry-controlled logical operation.", true),
            [Definition.Ports.Ack] = ("Signals", 1, "Completes the matching attempt successfully.", false),
            [Definition.Ports.Nak] = ("Signals", 2, "Fails the matching attempt and applies retry policy.", false),
            [Definition.Ports.Cancel] = ("Signals", 3, "Cancels the matching logical operation.", false),
            [Definition.Ports.Output] = ("Results", 4, "Attempt, scheduled retry, completion, exhaustion, cancellation, or rejection result.", true),
            [Definition.Ports.Events] = ("Diagnostics", 5, "Best-effort retry diagnostics.", false)
        };
        var resources = new Dictionary<string, (string Label, int Order, string Summary, string Picker, string ValueType)>(StringComparer.Ordinal)
        {
            [Definition.Resources.Clock] = ("Clock", 0, "Optional host-owned clock for deterministic attempt timeouts and retry delays.", ResourceDesignMetadataAttributeValues.Clock, nameof(TimeProvider)),
            [Definition.Resources.Jitter] = ("Jitter", 1, "Optional host-owned jitter sample source for deterministic retry timing.", ResourceDesignMetadataAttributeValues.Delegate, nameof(IRetryJitterSource))
        };
        return metadata with
        {
            DisplayName = new("Flow Retry"),
            Category = new("Resilience"),
            Summary = new("Coordinates acknowledged workflow attempts with retry, timeout, cancellation, and exhaustion results."),
            IconKey = new("refresh-cw"),
            PreferredNodeName = new("retry"),
            SuggestedEditorWidth = 460,
            Options = metadata.Options.Select(option => options.TryGetValue(option.Name.Value, out var describe)
                ? describe(option) : option).ToArray(),
            Ports = metadata.Ports.Select(port => ports.TryGetValue(port.Name.Value, out var hint)
                ? port with { Group = new(hint.Group), Order = hint.Order, Summary = new(hint.Summary), IsPrimary = hint.Primary }
                : port).ToArray(),
            Resources = metadata.Resources.Select(resource => resources.TryGetValue(resource.Name.Value, out var hint)
                ? resource with
                {
                    DisplayName = new(hint.Label), Order = hint.Order, Summary = new(hint.Summary),
                    ValueType = new(hint.ValueType),
                    Attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
                    {
                        [new(ResourceDesignMetadataAttributeNames.Ownership)] = new(ResourceDesignMetadataAttributeValues.HostOwned),
                        [new(ResourceDesignMetadataAttributeNames.PickerKind)] = new(hint.Picker),
                        [new(ResourceDesignMetadataAttributeNames.KeyPattern)] = new("Resources.{name}")
                    }
                } : resource).ToArray()
        };
    }

    private static OptionDesignMetadata DescribeOption(OptionDesignMetadata option,
        OptionValueKind kind, string label, string helperText, string section,
        string importance = OptionHints.Advanced, object? defaultValue = null,
        double? min = null, double? max = null, string? editor = null,
        string? syntax = null, string? relatedResource = null,
        IReadOnlyList<OptionChoiceMetadata>? choices = null)
    {
        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
        {
            [new(OptionDesignMetadataAttributeNames.Section)] = new(section),
            [new(OptionDesignMetadataAttributeNames.Importance)] = new(importance)
        };
        if (editor is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Editor), new(editor));
        if (syntax is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Syntax), new(syntax));
        if (relatedResource is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.RelatedResource), new(relatedResource));
        return option with
        {
            Kind = kind, DisplayName = new(label), HelperText = new(helperText),
            DefaultValue = defaultValue, Min = min, Max = max, Attributes = attributes,
            Choices = choices ?? option.Choices
        };
    }
}
