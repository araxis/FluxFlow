namespace FluxFlow.Components.Storage.Options;

public sealed record StorageDeleteOptions
{
    public string? Collection { get; init; }
    public bool EmitMissingAsResult { get; init; } = true;
    public int BoundedCapacity { get; init; } = 128;

    public static StorageDeleteOptions Default { get; } = new();
}
