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

    private static ComponentDesignMetadata CreateEventExpectationMetadata() => new()
    {
        Type = new ComponentType(ExpectationsCompositionNodeTypes.EventExpectation),
        DisplayName = "Event Expectation",
        Category = "Expectations",
        Summary = "Resolves once when a matching projection event appears, times out, or completes.",
        IconKey = "badge-check",
        PreferredNodeName = "expectEvent",
        SuggestedEditorWidth = 460,
        Options =
        [
            new OptionDesignMetadata
            {
                Name = "kind",
                Kind = OptionValueKind.Enum,
                DisplayName = "Kind",
                DefaultValue = Defaults.Kind.ToString(),
                HelperText = "Expectation behavior: expect a match or guard against one.",
                Choices =
                [
                    KindChoice(EventExpectationNodeKind.Expect, "Expect", "Satisfied when a matching event arrives."),
                    KindChoice(EventExpectationNodeKind.Guard, "Guard", "Satisfied when no matching event arrives.")
                ]
            },
            new OptionDesignMetadata
            {
                Name = "name",
                Kind = OptionValueKind.Text,
                DisplayName = "Name",
                HelperText = "Optional result name included in emitted expectation results."
            },
            new OptionDesignMetadata
            {
                Name = "filter",
                Kind = OptionValueKind.Json,
                DisplayName = "Filter",
                DefaultValue = Defaults.Filter,
                HelperText = "Event filter object for matching projection events."
            },
            new OptionDesignMetadata
            {
                Name = "timeoutMilliseconds",
                Kind = OptionValueKind.Number,
                DisplayName = "Timeout Milliseconds",
                Min = 0.000001,
                HelperText = "Optional timeout in milliseconds; when set it must be greater than zero."
            },
            new OptionDesignMetadata
            {
                Name = "maxObservedEvents",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Observed Events",
                DefaultValue = Defaults.MaxObservedEvents,
                Min = 0,
                HelperText = "Maximum recent observed event summaries retained in the result."
            },
            new OptionDesignMetadata
            {
                Name = "maxPreviewChars",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Preview Chars",
                DefaultValue = Defaults.MaxPreviewChars,
                Min = 0,
                HelperText = "Maximum observed payload preview characters; zero disables previews."
            },
            new OptionDesignMetadata
            {
                Name = "boundedCapacity",
                Kind = OptionValueKind.Number,
                DisplayName = "Bounded Capacity",
                DefaultValue = Defaults.BoundedCapacity,
                Min = 1,
                HelperText = "Maximum queued input messages."
            }
        ],
        Resources =
        [
            new ResourceDesignMetadata
            {
                Name = ExpectationsCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 0,
                Summary = "Optional keyed clock for deterministic expectation timeouts, results, and diagnostics.",
                ValueType = nameof(TimeProvider)
            }
        ],
        Ports =
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ExpectationsCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "Projection event observed by the expectation.",
                ValueType = nameof(ProjectionEvent),
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ExpectationsCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Results",
                Order = 1,
                Summary = "Event expectation result.",
                ValueType = nameof(EventExpectationResult),
                IsPrimary = true
            }
        ]
    };

    private static OptionChoiceMetadata KindChoice(
        EventExpectationNodeKind kind,
        string displayName,
        string helperText) => new()
        {
            Value = kind.ToString(),
            DisplayName = displayName,
            HelperText = helperText
        };
}
