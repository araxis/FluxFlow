using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Options;

namespace FluxFlow.Components.Projections.Composition;

public sealed class ProjectionsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly EventProjectionOptions Defaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateEventProjectionMetadata()];

    private static ComponentDesignMetadata CreateEventProjectionMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(ProjectionsCompositionNodeTypes.EventProjection)
            .WithDisplay(
                displayName: "Event Projection",
                category: "Projections",
                summary: "Folds matching projection events into count, latest-event, and rolling-rate snapshots.",
                iconKey: "activity",
                preferredNodeName: "projectEvents",
                suggestedEditorWidth: 460);

        AddEventProjectionOptions(builder);
        AddEventProjectionResources(builder);
        AddEventProjectionPorts(builder);

        return builder.Build();
    }

    private static void AddEventProjectionOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                "name",
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional snapshot name included in emitted projection snapshots.")
            .AddOption(
                "filter",
                OptionValueKind.Json,
                displayName: "Filter",
                helperText: "Event filter object for matching projection events.",
                defaultValue: Defaults.Filter)
            .AddOption(
                "rateWindowSeconds",
                OptionValueKind.Number,
                displayName: "Rate Window Seconds",
                helperText: "Rolling rate window in seconds; must be greater than zero.",
                defaultValue: Defaults.RateWindowSeconds,
                min: 0.000001)
            .AddOption(
                "emitEveryMatch",
                OptionValueKind.Boolean,
                displayName: "Emit Every Match",
                helperText: "Emit a snapshot after each matching event.",
                defaultValue: Defaults.EmitEveryMatch)
            .AddOption(
                "emitFinalSnapshot",
                OptionValueKind.Boolean,
                displayName: "Emit Final Snapshot",
                helperText: "Direct-node lifecycle option for final snapshots; composition runtime stop uses normal completion.",
                defaultValue: Defaults.EmitFinalSnapshot)
            .AddOption(
                "maxPreviewChars",
                OptionValueKind.Number,
                displayName: "Max Preview Chars",
                helperText: "Maximum latest payload preview characters; zero disables previews.",
                defaultValue: Defaults.MaxPreviewChars,
                min: 0)
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: Defaults.BoundedCapacity,
                min: 1);

    private static void AddEventProjectionResources(ComponentDesignMetadataBuilder builder)
        => builder.AddResource(
            ProjectionsCompositionResourceNames.Clock,
            displayName: "Clock",
            order: 0,
            summary: "Optional keyed clock for deterministic projection snapshot timestamps and diagnostics.",
            valueType: nameof(TimeProvider));

    private static void AddEventProjectionPorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                ProjectionsCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Projection event to fold into the running snapshot.",
                valueType: nameof(ProjectionEvent),
                isPrimary: true)
            .AddOutputPort(
                ProjectionsCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Event projection snapshot.",
                valueType: nameof(EventProjectionSnapshot),
                isPrimary: true);
}
