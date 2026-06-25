namespace FluxFlow.Components.Storage.Options;

public sealed record StorageQueryOptions
{
    private string? _collection;
    private int _offset;
    private int _limit = 100;
    private int _boundedCapacity = 128;

    public string? Collection
    {
        get => _collection;
        init => _collection = StorageOptionValidation.NormalizeCollection(value);
    }

    public bool IncludeExpired { get; init; }

    public int Offset
    {
        get => _offset;
        init => _offset = StorageOptionValidation.ValidateOffset(value);
    }

    public int Limit
    {
        get => _limit;
        init => _limit = StorageOptionValidation.ValidateLimit(value);
    }

    public bool EmitRecordsInResult { get; init; } = true;
    public bool EmitRecordOutputs { get; init; } = true;

    public int BoundedCapacity
    {
        get => _boundedCapacity;
        init => _boundedCapacity = StorageOptionValidation.ValidateBoundedCapacity(value);
    }

    public static StorageQueryOptions Default { get; } = new();
}
