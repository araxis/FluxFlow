namespace FluxFlow.Components.Storage.Options;

public sealed record StorageQueryOptions
{
    public string? Store { get; init; }
    public string? Collection { get; init; }
    public bool IncludeExpired { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 100;
    public bool EmitRecordsInResult { get; init; } = true;
    public bool EmitRecordOutputs { get; init; } = true;
    public int BoundedCapacity { get; init; } = 128;
}
