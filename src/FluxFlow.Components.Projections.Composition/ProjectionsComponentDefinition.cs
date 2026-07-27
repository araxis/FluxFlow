using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Options;

namespace FluxFlow.Components.Projections.Composition;

public static partial class ProjectionsComponentDefinition
{
    private static readonly EventProjectionOptions Defaults = new();

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        => [CreateEventProjectionMetadata()];

    private static ComponentDesignMetadata CreateEventProjectionMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(ProjectionsComponentDefinition.Types.EventProjection)
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
                Options.Name,
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional snapshot name included in emitted projection snapshots.",
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
                Options.RateWindowSeconds,
                OptionValueKind.Number,
                displayName: "Rate Window Seconds",
                helperText: "Rolling rate window in seconds; must be greater than zero.",
                defaultValue: Defaults.RateWindowSeconds,
                min: 0.000001,
                attributes: OptionAttributes(
                    "Rate",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.EmitEveryMatch,
                OptionValueKind.Boolean,
                displayName: "Emit Every Match",
                helperText: "Emit a snapshot after each matching event.",
                defaultValue: Defaults.EmitEveryMatch,
                attributes: OptionAttributes(
                    "Emission",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.EmitFinalSnapshot,
                OptionValueKind.Boolean,
                displayName: "Emit Final Snapshot",
                helperText: "Emit one final snapshot after accepted input drains on completion.",
                defaultValue: Defaults.EmitFinalSnapshot,
                attributes: OptionAttributes(
                    "Emission",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.MaxPreviewChars,
                OptionValueKind.Number,
                displayName: "Max Preview Chars",
                helperText: "Maximum latest payload preview characters; zero disables previews.",
                defaultValue: Defaults.MaxPreviewChars,
                min: 0,
                attributes: OptionAttributes(
                    "Preview",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(Defaults.BoundedCapacity));

    private static void AddEventProjectionResources(ComponentDesignMetadataBuilder builder)
        => builder.AddResource(
            ProjectionsComponentDefinition.Resources.Clock,
            displayName: "Clock",
            order: 0,
            summary: "Optional keyed clock for deterministic projection snapshot timestamps and diagnostics.",
            valueType: nameof(TimeProvider),
            attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                ResourceDesignMetadataAttributeValues.Clock,
                keyPattern: "clock:{name}"));

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

    private static void AddEventProjectionPorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                ProjectionsComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: "Projection event to fold into the running snapshot.",
                valueType: nameof(ProjectionEvent),
                isPrimary: true)
            .AddOutputPort(
                ProjectionsComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "Event projection snapshot.",
                valueType: nameof(EventProjectionSnapshot),
                isPrimary: true);


    public static class Options
    {
        public const string Name = "name";
        public const string Filter = "filter";
        public const string RateWindowSeconds = "rateWindowSeconds";
        public const string EmitEveryMatch = "emitEveryMatch";
        public const string EmitFinalSnapshot = "emitFinalSnapshot";
        public const string MaxPreviewChars = "maxPreviewChars";
        public const string BoundedCapacity = "boundedCapacity";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.EventProjection =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<EventFilter>(Options.Filter),
                ComponentOptions.Metadata<double>(Options.RateWindowSeconds),
                ComponentOptions.Metadata<bool>(Options.EmitEveryMatch),
                ComponentOptions.Metadata<bool>(Options.EmitFinalSnapshot),
                ComponentOptions.Metadata<int>(Options.MaxPreviewChars),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.EventProjection =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string EventProjection = "event.project";
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
