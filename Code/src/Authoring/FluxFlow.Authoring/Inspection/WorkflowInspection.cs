namespace FluxFlow.Authoring;

/// <summary>A detached workflow view. Links include known incoming and outgoing cross-workflow declarations.</summary>
public sealed record WorkflowInspection(string Name, IReadOnlyList<ComponentInspection> Components,
    IReadOnlyList<ConfigurationLink> Links, IReadOnlyList<ValidationMessageModel> Diagnostics);

/// <summary>Unknown components retain their configuration and have no presentation or Inspector metadata.</summary>
public sealed record ComponentInspection(ConfigurationNode Configuration,
    PaletteItemModel? Presentation, NodeInspectorModel? Inspector);
