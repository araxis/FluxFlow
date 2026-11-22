namespace FluxFlow.Plugins;

/// <summary>A selectable item in a design-time hierarchy. A branch may populate its children when expanded.</summary>
public sealed record ExplorerItem<T>(T? Value, string Label, ExplorerGlyph Glyph) where T : class
{
    public string? Detail { get; init; }
    public string? Hint { get; init; }
    public IReadOnlyList<ExplorerItem<T>> Children { get; init; } = [];
    public bool Expanded { get; init; }
    public bool LoadOnExpand { get; init; }
}

public enum ExplorerGlyph { None, Device, Folder, Value }
