using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;

namespace FluxFlow.Components.Expectations.Composition;

public static partial class ExpectationsComponentDefinition
{
    private static readonly EventExpectationOptions Defaults = new();

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        => [CreateEventExpectationMetadata()];

    private static ComponentDesignMetadata CreateEventExpectationMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(ExpectationsComponentDefinition.Types.EventExpectation)
            .WithDisplay(
                displayName: "Event Expectation",
                category: "Expectations",
                summary: "Resolves projection-event rules, timeout, completion, and evaluation failures through one result output.",
                iconKey: "badge-check",
                preferredNodeName: "expectEvent",
                suggestedEditorWidth: 460);

        AddEventExpectationOptions(builder);
        AddEventExpectationResources(builder);
        AddEventExpectationPorts(builder);

        return builder.Build();
    }

    private static void AddEventExpectationOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                Options.Kind,
                OptionValueKind.Enum,
                displayName: "Kind",
                helperText: "Expectation behavior: expect a match or guard against one.",
                defaultValue: Defaults.Kind.ToString(),
                choices:
                [
                    KindChoice(EventExpectationNodeKind.Expect, "Expect", "Satisfied when a matching event arrives."),
                    KindChoice(EventExpectationNodeKind.Guard, "Guard", "Satisfied when no matching event arrives.")
                ],
                attributes: OptionAttributes(
                    "Expectation",
                    OptionDesignMetadataAttributeValues.Primary))
            .AddOption(
                Options.Name,
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional result name included in emitted expectation results.",
                attributes: OptionAttributes(
                    "Diagnostics",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.Filter,
                OptionValueKind.Json,
                displayName: "Filter",
                helperText: "Event filter object for matching projection events.",
                defaultValue: Defaults.Filter,
                attributes: OptionAttributes(
                    "Filtering",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Json))
            .AddOption(
                Options.TimeoutMilliseconds,
                OptionValueKind.Number,
                displayName: "Timeout Milliseconds",
                helperText: "Optional timeout in milliseconds; when set it must be greater than zero.",
                min: 0.000001,
                attributes: OptionAttributes(
                    "Runtime",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.MaxObservedEvents,
                OptionValueKind.Number,
                displayName: "Max Observed Events",
                helperText: "Maximum recent observed event summaries retained in the result.",
                defaultValue: Defaults.MaxObservedEvents,
                min: 0,
                attributes: OptionAttributes(
                    "Results",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.MaxPreviewChars,
                OptionValueKind.Number,
                displayName: "Max Preview Chars",
                helperText: "Maximum observed payload preview characters; zero disables previews.",
                defaultValue: Defaults.MaxPreviewChars,
                min: 0,
                attributes: OptionAttributes(
                    "Preview",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(Defaults.BoundedCapacity));

    private static void AddEventExpectationResources(ComponentDesignMetadataBuilder builder)
        => builder.AddResource(
            ExpectationsComponentDefinition.Resources.Clock,
            displayName: "Clock",
            order: 0,
            summary: "Optional keyed clock for deterministic expectation timeouts, results, and diagnostics.",
            valueType: nameof(TimeProvider),
            attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                ResourceDesignMetadataAttributeValues.Clock,
                keyPattern: "Resources.{name}"));

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

    private static void AddEventExpectationPorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                ExpectationsComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: "Projection event observed by the expectation.",
                valueType: nameof(ProjectionEvent),
                isPrimary: true)
            .AddOutputPort(
                ExpectationsComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "Normal matched, unmet, timeout, completion, or evaluation-failure result.",
                valueType: nameof(EventExpectationResult),
                isPrimary: true);

    private static OptionChoiceMetadata KindChoice(
        EventExpectationNodeKind kind,
        string displayName,
        string helperText) => new()
        {
            Value = new ComponentOptionChoiceValue(kind.ToString()),
            DisplayName = new ComponentMetadataText(displayName),
            HelperText = new ComponentMetadataText(helperText)
        };


    public static class Options
    {
        public const string Kind = "kind";
        public const string Name = "name";
        public const string Filter = "filter";
        public const string TimeoutMilliseconds = "timeoutMilliseconds";
        public const string MaxObservedEvents = "maxObservedEvents";
        public const string MaxPreviewChars = "maxPreviewChars";
        public const string BoundedCapacity = "boundedCapacity";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.EventExpectation =>
            [
                ComponentOptions.Metadata<EventExpectationNodeKind>(Options.Kind),
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<EventFilter>(Options.Filter),
                ComponentOptions.Metadata<double?>(Options.TimeoutMilliseconds),
                ComponentOptions.Metadata<int>(Options.MaxObservedEvents),
                ComponentOptions.Metadata<int>(Options.MaxPreviewChars),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.EventExpectation =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string EventExpectation = "event.expect";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Clock = "clock";
    }
}
