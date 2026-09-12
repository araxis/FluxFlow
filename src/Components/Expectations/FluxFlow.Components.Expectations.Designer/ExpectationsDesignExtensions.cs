using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Composition;
using Definition = FluxFlow.Components.Expectations.Composition.ExpectationsComponentDefinition;
using OptionHints = FluxFlow.Components.Designer.Contracts.OptionDesignMetadataAttributeValues;

namespace FluxFlow.Components.Expectations.Designer;

public static class ExpectationsDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddExpectationsDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignMetadata(Definition.Types.EventExpectation, Describe);
    }

    private static ComponentDesignMetadata Describe(ComponentDesignMetadata metadata)
    {
        var defaults = new EventExpectationOptions();
        var options = new Dictionary<string, Func<OptionDesignMetadata, OptionDesignMetadata>>(StringComparer.Ordinal)
        {
            [Definition.Options.Kind] = option => DescribeOption(option, OptionValueKind.Enum,
                "Kind", "Expectation behavior: expect a match or guard against one.", "Expectation",
                defaultValue: defaults.Kind.ToString(), importance: OptionHints.Primary, choices:
            [
                new() { Value = new(nameof(EventExpectationNodeKind.Expect)), DisplayName = new("Expect"), HelperText = new("Satisfied when a matching event arrives.") },
                new() { Value = new(nameof(EventExpectationNodeKind.Guard)), DisplayName = new("Guard"), HelperText = new("Satisfied when no matching event arrives.") }
            ]),
            [Definition.Options.Name] = option => DescribeOption(option, OptionValueKind.Text,
                "Name", "Optional result name included in emitted expectation results.", "Diagnostics",
                editor: OptionHints.Text),
            [Definition.Options.Filter] = option => DescribeOption(option, OptionValueKind.Json,
                "Filter", "Event filter object for matching projection events.", "Filtering",
                defaultValue: defaults.Filter, importance: OptionHints.Primary, editor: OptionHints.Json),
            [Definition.Options.TimeoutMilliseconds] = option => DescribeOption(option, OptionValueKind.Number,
                "Timeout Milliseconds", "Optional timeout in milliseconds; when set it must be greater than zero.", "Runtime",
                min: 0.000001, editor: OptionHints.Number),
            [Definition.Options.MaxObservedEvents] = option => DescribeOption(option, OptionValueKind.Number,
                "Max Observed Events", "Maximum recent observed event summaries retained in the result.", "Results",
                defaultValue: defaults.MaxObservedEvents, min: 0, editor: OptionHints.Number),
            [Definition.Options.MaxPreviewChars] = option => DescribeOption(option, OptionValueKind.Number,
                "Max Preview Chars", "Maximum observed payload preview characters; zero disables previews.", "Preview",
                defaultValue: defaults.MaxPreviewChars, min: 0, editor: OptionHints.Number),
            [Definition.Options.BoundedCapacity] = option => DescribeOption(option, OptionValueKind.Number,
                "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", "Runtime",
                defaultValue: defaults.BoundedCapacity, min: 1, editor: OptionHints.Number)
        };
        var ports = new Dictionary<string, (string Group, int Order, string Summary, bool Primary)>(StringComparer.Ordinal)
        {
            [Definition.Ports.Input] = ("Messages", 0, "Projection event observed by the expectation.", true),
            [Definition.Ports.Output] = ("Results", 1, "Normal matched, unmet, timeout, completion, or evaluation-failure result.", true),
            [Definition.Ports.Events] = ("Diagnostics", 2, "Best-effort expectation diagnostics.", false)
        };
        var resources = new Dictionary<string, (string Label, int Order, string Summary, string Picker, string ValueType)>(StringComparer.Ordinal)
        {
            [Definition.Resources.Clock] = ("Clock", 0, "Optional keyed clock for deterministic expectation timeouts, results, and diagnostics.", ResourceDesignMetadataAttributeValues.Clock, nameof(TimeProvider))
        };
        return metadata with
        {
            DisplayName = new("Event Expectation"),
            Category = new("Expectations"),
            Summary = new("Resolves projection-event rules, timeout, completion, and evaluation failures through one result output."),
            IconKey = new("badge-check"),
            PreferredNodeName = new("expectEvent"),
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
