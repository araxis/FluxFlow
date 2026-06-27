namespace FluxFlow.Components.Designer.Contracts;

public sealed record ComponentDesignMetadata
{
    public required ComponentType Type { get; init; }
    public string? DisplayName { get; init; }
    public ComponentCategory? Category { get; init; }
    public string? Summary { get; init; }
    public ComponentIconKey? IconKey { get; init; }
    public string? PreferredNodeName { get; init; }
    public int? SuggestedEditorWidth { get; init; }
    public IReadOnlyList<OptionDesignMetadata> Options { get; init; } = [];
    public IReadOnlyList<ResourceDesignMetadata> Resources { get; init; } = [];
    public IReadOnlyList<PortDesignMetadata> Ports { get; init; } = [];
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
}
