namespace FluxFlow.Plugins;

public sealed record ConnectionPreview(string? Endpoint, IReadOnlyList<ConnectionPreviewField> Fields);
public sealed record ConnectionPreviewField(string Name, string Value, bool FromEndpoint = false)
{
    /// <summary>The configuration key represented by this field, independent of its display name.</summary>
    public string? SettingKey { get; init; }
}
