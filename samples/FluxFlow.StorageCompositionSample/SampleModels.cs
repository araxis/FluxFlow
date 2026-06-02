using FluxFlow.Components.Storage.Contracts;

namespace FluxFlow.StorageCompositionSample;

internal sealed record SamplePutRecord(string Key, string Value);

internal sealed record CapturedStorageResult(
    string Stage,
    string Operation,
    string Collection,
    string Key,
    bool Succeeded,
    bool Found,
    bool Deleted,
    long? Version,
    object? Value)
{
    public static CapturedStorageResult FromResult(string stage, StorageResult result)
        => new(
            stage,
            result.Operation,
            result.Collection,
            result.Key,
            result.Succeeded,
            result.Found,
            result.Deleted,
            result.Version,
            result.Record?.Value);
}

internal sealed record CapturedStorageQueryResult(
    string Stage,
    string Operation,
    string Collection,
    int Count,
    IReadOnlyList<string> Keys)
{
    public static CapturedStorageQueryResult FromResult(string stage, StorageQueryResult result)
        => new(
            stage,
            result.Operation,
            result.Collection,
            result.Count,
            result.Records.Select(record => record.Key).ToArray());
}

internal sealed class SampleCapture
{
    private readonly object _gate = new();
    private readonly List<CapturedStorageResult> _results = [];
    private readonly List<CapturedStorageQueryResult> _queryResults = [];

    public void Add(string stage, StorageResult result)
    {
        lock (_gate)
        {
            _results.Add(CapturedStorageResult.FromResult(stage, result));
        }
    }

    public void Add(string stage, StorageQueryResult result)
    {
        lock (_gate)
        {
            _queryResults.Add(CapturedStorageQueryResult.FromResult(stage, result));
        }
    }

    public IReadOnlyList<CapturedStorageResult> GetResults()
    {
        lock (_gate)
        {
            return [.. _results];
        }
    }

    public IReadOnlyList<CapturedStorageQueryResult> GetQueryResults()
    {
        lock (_gate)
        {
            return [.. _queryResults];
        }
    }
}
