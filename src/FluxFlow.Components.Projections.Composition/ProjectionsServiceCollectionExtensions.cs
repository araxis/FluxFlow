using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Nodes;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Projections.Composition;

public static class ProjectionsServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddProjections(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignedComponent(ProjectionsComponents.EventProjection);
    }

    internal static void ConfigureProjection(ComponentRegistrationBuilder component)
    {
        var defaults = new EventProjectionOptions();
        component.WithDisplay("Event Projection", "Projections", "Folds matching projection events into count, latest-event, and rolling-rate snapshots.", "activity", "projectEvents", 460);
        component
            .UseFactory(CreateEventProjectionNode)
            .HasInput(ProjectionsComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Projection event to fold into the running snapshot.", true)
            .HasOutput(ProjectionsComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Event projection snapshot.", true)
            .HasEvents(ProjectionsComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort projection diagnostics.");
        component.AddOption<string>(ProjectionsComponentDefinition.Options.Name, OptionValueKind.Text, "Name", "Optional snapshot name included in emitted projection snapshots.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<EventFilter>(ProjectionsComponentDefinition.Options.Filter, OptionValueKind.Json, "Filter", "Event filter object for matching projection events.", defaultValue: defaults.Filter, section: "Filtering", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Json);
        component.AddOption<double>(ProjectionsComponentDefinition.Options.RateWindowSeconds, OptionValueKind.Number, "Rate Window Seconds", "Rolling rate window in seconds; must be greater than zero.", defaultValue: defaults.RateWindowSeconds, min: 0.000001, section: "Rate", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        AddBoolean(component, ProjectionsComponentDefinition.Options.EmitEveryMatch, "Emit Every Match", "Emit a snapshot after each matching event.", defaults.EmitEveryMatch);
        AddBoolean(component, ProjectionsComponentDefinition.Options.EmitFinalSnapshot, "Emit Final Snapshot", "Emit one final snapshot after accepted input drains on completion.", defaults.EmitFinalSnapshot);
        component.AddOption<int>(ProjectionsComponentDefinition.Options.MaxPreviewChars, OptionValueKind.Number, "Max Preview Chars", "Maximum latest payload preview characters; zero disables previews.", defaultValue: defaults.MaxPreviewChars, min: 0, section: "Preview", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(ProjectionsComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddResource<TimeProvider>(ProjectionsComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic projection snapshot timestamps and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
    }

    private static void AddBoolean(ComponentRegistrationBuilder component, string name, string displayName, string helperText, bool defaultValue)
        => component.AddOption<bool>(name, OptionValueKind.Boolean, displayName, helperText, defaultValue: defaultValue, section: "Emission", importance: OptionDesignMetadataAttributeValues.Advanced);

    private static EventProjectionNode CreateEventProjectionNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<EventProjectionOptions>();
        var clock = context.GetResource<TimeProvider>(
            ProjectionsComponentDefinition.Resources.Clock);
        return new EventProjectionNode(options, clock);
    }
}
