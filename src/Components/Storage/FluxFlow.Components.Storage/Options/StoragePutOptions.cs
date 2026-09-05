using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.Components.Storage.Options;

public sealed record StoragePutOptions
{
    private string? _collection;
    private StorageWriteMode _mode = StorageWriteMode.Upsert;
    private int _boundedCapacity = 128;

    public string? Collection
    {
        get => _collection;
        init => _collection = StorageOptionValidation.NormalizeCollection(value);
    }

    public StorageWriteMode Mode
    {
        get => _mode;
        init => _mode = StorageOptionValidation.ValidateMode(value);
    }

    public bool EmitStoredRecord { get; init; } = true;

    public int BoundedCapacity
    {
        get => _boundedCapacity;
        init => _boundedCapacity = StorageOptionValidation.ValidateBoundedCapacity(value);
    }

    public static StoragePutOptions Default { get; } = new();
}
