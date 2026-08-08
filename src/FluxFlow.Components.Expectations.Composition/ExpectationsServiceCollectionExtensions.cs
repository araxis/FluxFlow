using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Expectations.Composition;

public static class ExpectationsServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddExpectations(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignedComponent(ExpectationsComponents.EventExpectation);
    }

    internal static void ConfigureEventExpectation(ComponentRegistrationBuilder component)
    {
        var defaults = new EventExpectationOptions();
        component.WithDisplay("Event Expectation", "Expectations", "Resolves projection-event rules, timeout, completion, and evaluation failures through one result output.", "badge-check", "expectEvent", 460);
        component
            .UseFactory(CreateEventExpectationNode)
            .HasInput(ExpectationsComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Projection event observed by the expectation.", true)
            .HasOutput(ExpectationsComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Normal matched, unmet, timeout, completion, or evaluation-failure result.", true)
            .HasEvents(ExpectationsComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort expectation diagnostics.");
        component.AddOption<EventExpectationNodeKind>(ExpectationsComponentDefinition.Options.Kind, OptionValueKind.Enum, "Kind", "Expectation behavior: expect a match or guard against one.", defaultValue: defaults.Kind.ToString(), section: "Expectation", importance: OptionDesignMetadataAttributeValues.Primary);
        component.AddOptionChoice(ExpectationsComponentDefinition.Options.Kind, EventExpectationNodeKind.Expect.ToString(), "Expect", "Satisfied when a matching event arrives.");
        component.AddOptionChoice(ExpectationsComponentDefinition.Options.Kind, EventExpectationNodeKind.Guard.ToString(), "Guard", "Satisfied when no matching event arrives.");
        component.AddOption<string>(ExpectationsComponentDefinition.Options.Name, OptionValueKind.Text, "Name", "Optional result name included in emitted expectation results.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<EventFilter>(ExpectationsComponentDefinition.Options.Filter, OptionValueKind.Json, "Filter", "Event filter object for matching projection events.", defaultValue: defaults.Filter, section: "Filtering", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Json);
        component.AddOption<double?>(ExpectationsComponentDefinition.Options.TimeoutMilliseconds, OptionValueKind.Number, "Timeout Milliseconds", "Optional timeout in milliseconds; when set it must be greater than zero.", min: 0.000001, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(ExpectationsComponentDefinition.Options.MaxObservedEvents, OptionValueKind.Number, "Max Observed Events", "Maximum recent observed event summaries retained in the result.", defaultValue: defaults.MaxObservedEvents, min: 0, section: "Results", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(ExpectationsComponentDefinition.Options.MaxPreviewChars, OptionValueKind.Number, "Max Preview Chars", "Maximum observed payload preview characters; zero disables previews.", defaultValue: defaults.MaxPreviewChars, min: 0, section: "Preview", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(ExpectationsComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddResource<TimeProvider>(ExpectationsComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic expectation timeouts, results, and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
    }

    private static EventExpectationNode CreateEventExpectationNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<EventExpectationOptions>();
        var clock = context.GetResource<TimeProvider>(
            ExpectationsComponentDefinition.Resources.Clock);
        return new EventExpectationNode(options, clock);
    }
}
