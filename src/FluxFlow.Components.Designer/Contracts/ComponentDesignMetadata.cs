using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Designer.Contracts;

public sealed record ComponentDesignMetadata
{
    public required NodeType Type { get; init; }
    public string? DisplayName { get; init; }
    public string? Category { get; init; }
    public string? Summary { get; init; }
    public string? IconKey { get; init; }
    public string? PreferredNodeName { get; init; }
    public int? SuggestedEditorWidth { get; init; }
    public IReadOnlyList<OptionDesignMetadata> Options { get; init; } = [];
    public IReadOnlyList<PortDesignMetadata> Ports { get; init; } = [];
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
}
