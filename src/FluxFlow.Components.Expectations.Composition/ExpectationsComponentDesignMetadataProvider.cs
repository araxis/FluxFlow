using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;

namespace FluxFlow.Components.Expectations.Composition;

public sealed class ExpectationsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly EventExpectationOptions Defaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateEventExpectationMetadata()];

    private static ComponentDesignMetadata CreateEventExpectationMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(ExpectationsCompositionNodeTypes.EventExpectation)
            .WithDisplay(
                displayName: "Event Expectation",
                category: "Expectations",
                summary: "Resolves once when a matching projection event appears, times out, or completes.",
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
                "kind",
                OptionValueKind.Enum,
                displayName: "Kind",
                helperText: "Expectation behavior: expect a match or guard against one.",
                defaultValue: Defaults.Kind.ToString(),
                choices:
                [
                    KindChoice(EventExpectationNodeKind.Expect, "Expect", "Satisfied when a matching event arrives."),
                    KindChoice(EventExpectationNodeKind.Guard, "Guard", "Satisfied when no matching event arrives.")
                ])
            .AddOption(
                "name",
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional result name included in emitted expectation results.")
            .AddOption(
                "filter",
                OptionValueKind.Json,
                displayName: "Filter",
                helperText: "Event filter object for matching projection events.",
                defaultValue: Defaults.Filter)
            .AddOption(
                "timeoutMilliseconds",
                OptionValueKind.Number,
                displayName: "Timeout Milliseconds",
                helperText: "Optional timeout in milliseconds; when set it must be greater than zero.",
                min: 0.000001)
            .AddOption(
                "maxObservedEvents",
                OptionValueKind.Number,
                displayName: "Max Observed Events",
                helperText: "Maximum recent observed event summaries retained in the result.",
                defaultValue: Defaults.MaxObservedEvents,
                min: 0)
            .AddOption(
                "maxPreviewChars",
                OptionValueKind.Number,
                displayName: "Max Preview Chars",
                helperText: "Maximum observed payload preview characters; zero disables previews.",
                defaultValue: Defaults.MaxPreviewChars,
                min: 0)
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: Defaults.BoundedCapacity,
                min: 1);

    private static void AddEventExpectationResources(ComponentDesignMetadataBuilder builder)
        => builder.AddResource(
            ExpectationsCompositionResourceNames.Clock,
            displayName: "Clock",
            order: 0,
            summary: "Optional keyed clock for deterministic expectation timeouts, results, and diagnostics.",
            valueType: nameof(TimeProvider));

    private static void AddEventExpectationPorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                ExpectationsCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Projection event observed by the expectation.",
                valueType: nameof(ProjectionEvent),
                isPrimary: true)
            .AddOutputPort(
                ExpectationsCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Event expectation result.",
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
}
