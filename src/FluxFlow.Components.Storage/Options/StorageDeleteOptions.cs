namespace FluxFlow.Components.Storage.Options;

public sealed record StorageDeleteOptions
{
    public string? Store { get; init; }
    public string? Collection { get; init; }
    public bool EmitMissingAsResult { get; init; } = true;
    public int BoundedCapacity { get; init; } = 128;
}
