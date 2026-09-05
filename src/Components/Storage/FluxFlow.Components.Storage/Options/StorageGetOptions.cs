namespace FluxFlow.Components.Storage.Options;

public sealed record StorageGetOptions
{
    private string? _collection;
    private int _boundedCapacity = 128;

    public string? Collection
    {
        get => _collection;
        init => _collection = StorageOptionValidation.NormalizeCollection(value);
    }

    public bool IncludeExpired { get; init; }

    public int BoundedCapacity
    {
        get => _boundedCapacity;
        init => _boundedCapacity = StorageOptionValidation.ValidateBoundedCapacity(value);
    }

    public static StorageGetOptions Default { get; } = new();
}
