using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.Components.Storage.Options;

public sealed record StoragePutOptions
{
    public string? Collection { get; init; }
    public StorageWriteMode Mode { get; init; } = StorageWriteMode.Upsert;
    public bool EmitStoredRecord { get; init; } = true;
    public int BoundedCapacity { get; init; } = 128;

    public static StoragePutOptions Default { get; } = new();
}
