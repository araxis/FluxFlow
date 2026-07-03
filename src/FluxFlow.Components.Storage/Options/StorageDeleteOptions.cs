namespace FluxFlow.Components.Storage.Options;

public sealed record StorageDeleteOptions
{
    private string? _collection;
    private int _boundedCapacity = 128;

    public string? Collection
    {
        get => _collection;
        init => _collection = StorageOptionValidation.NormalizeCollection(value);
    }

    public bool EmitMissingAsResult { get; init; } = true;

    public int BoundedCapacity
    {
        get => _boundedCapacity;
        init => _boundedCapacity = StorageOptionValidation.ValidateBoundedCapacity(value);
    }

    public static StorageDeleteOptions Default { get; } = new();
}
