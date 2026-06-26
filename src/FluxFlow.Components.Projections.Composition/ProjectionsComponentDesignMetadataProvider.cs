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

    private static ComponentDesignMetadata CreateEventProjectionMetadata() => new()
    {
        Type = new ComponentType(ProjectionsCompositionNodeTypes.EventProjection),
        DisplayName = "Event Projection",
        Category = "Projections",
        Summary = "Folds matching projection events into count, latest-event, and rolling-rate snapshots.",
        IconKey = "activity",
        PreferredNodeName = "projectEvents",
        SuggestedEditorWidth = 460,
        Options = EventProjectionOptionsMetadata(),
        Resources = EventProjectionResources(),
        Ports = EventProjectionPorts()
    };

    private static IReadOnlyList<OptionDesignMetadata> EventProjectionOptionsMetadata()
        =>
        [
            new OptionDesignMetadata
            {
                Name = "name",
                Kind = OptionValueKind.Text,
                DisplayName = "Name",
                HelperText = "Optional snapshot name included in emitted projection snapshots."
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
                Name = "rateWindowSeconds",
                Kind = OptionValueKind.Number,
                DisplayName = "Rate Window Seconds",
                DefaultValue = Defaults.RateWindowSeconds,
                Min = 0.000001,
                HelperText = "Rolling rate window in seconds; must be greater than zero."
            },
            new OptionDesignMetadata
            {
                Name = "emitEveryMatch",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Emit Every Match",
                DefaultValue = Defaults.EmitEveryMatch,
                HelperText = "Emit a snapshot after each matching event."
            },
            new OptionDesignMetadata
            {
                Name = "emitFinalSnapshot",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Emit Final Snapshot",
                DefaultValue = Defaults.EmitFinalSnapshot,
                HelperText = "Direct-node lifecycle option for final snapshots; composition runtime stop uses normal completion."
            },
            new OptionDesignMetadata
            {
                Name = "maxPreviewChars",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Preview Chars",
                DefaultValue = Defaults.MaxPreviewChars,
                Min = 0,
                HelperText = "Maximum latest payload preview characters; zero disables previews."
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
        ];

    private static IReadOnlyList<ResourceDesignMetadata> EventProjectionResources()
        =>
        [
            new ResourceDesignMetadata
            {
                Name = ProjectionsCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 0,
                Summary = "Optional keyed clock for deterministic projection snapshot timestamps and diagnostics.",
                ValueType = nameof(TimeProvider)
            }
        ];

    private static IReadOnlyList<PortDesignMetadata> EventProjectionPorts()
        =>
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ProjectionsCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "Projection event to fold into the running snapshot.",
                ValueType = nameof(ProjectionEvent),
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ProjectionsCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Results",
                Order = 1,
                Summary = "Event projection snapshot.",
                ValueType = nameof(EventProjectionSnapshot),
                IsPrimary = true
            }
        ];
}
