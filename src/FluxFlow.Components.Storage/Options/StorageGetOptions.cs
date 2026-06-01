namespace FluxFlow.Components.Storage.Options;

public sealed record StorageGetOptions
{
    public string? Store { get; init; }
    public string? Collection { get; init; }
    public bool IncludeExpired { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
