using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Projections.Composition;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Projections.Designer;

public static class ProjectionsDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddProjectionsDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(ProjectionsComponentDefinition.Types.EventProjection, DescribeProjection);
    }

    private static ComponentDesignMetadata DescribeProjection(ComponentDesignMetadata metadata)
    {
        var defaults = new EventProjectionOptions();
        metadata = metadata.WithDisplay("Event Projection", "Projections", "Folds matching projection events into count, latest-event, and rolling-rate snapshots.", "activity", "projectEvents", 460);
        metadata = metadata
            .WithPort(ProjectionsComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Projection event to fold into the running snapshot.", true)
            .WithPort(ProjectionsComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Event projection snapshot.", true)
            .WithPort(ProjectionsComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort projection diagnostics.");
        metadata = metadata.WithOption(ProjectionsComponentDefinition.Options.Filter, OptionValueKind.Json, "Filter", "Event filter object for matching projection events.", defaultValue: defaults.Filter, section: "Filtering", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Json);
        metadata = metadata.WithOption(ProjectionsComponentDefinition.Options.RateWindowSeconds, OptionValueKind.Number, "Rate Window Seconds", "Rolling rate window in seconds; must be greater than zero.", defaultValue: defaults.RateWindowSeconds, min: 0.000001, section: "Rate", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(ProjectionsComponentDefinition.Options.EmitEveryMatch, OptionValueKind.Boolean, "Emit Every Match", "Emit a snapshot after each matching event.", defaultValue: defaults.EmitEveryMatch, section: "Emission", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(ProjectionsComponentDefinition.Options.EmitFinalSnapshot, OptionValueKind.Boolean, "Emit Final Snapshot", "Emit one final snapshot after accepted input drains on completion.", defaultValue: defaults.EmitFinalSnapshot, section: "Emission", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(ProjectionsComponentDefinition.Options.MaxPreviewChars, OptionValueKind.Number, "Max Preview Chars", "Maximum latest payload preview characters; zero disables previews.", defaultValue: defaults.MaxPreviewChars, min: 0, section: "Preview", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(ProjectionsComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithResource(ProjectionsComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic projection snapshot timestamps and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        return metadata;
    }
}
