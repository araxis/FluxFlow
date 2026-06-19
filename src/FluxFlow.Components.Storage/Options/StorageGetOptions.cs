namespace FluxFlow.Components.Storage.Options;

public sealed record StorageGetOptions
{
    public string? Collection { get; init; }
    public bool IncludeExpired { get; init; }
    public int BoundedCapacity { get; init; } = 128;

    public static StorageGetOptions Default { get; } = new();
}
