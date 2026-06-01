using FluxFlow.Components.Sessions.Contracts;

namespace FluxFlow.SessionsCompositionSample;

internal sealed record CapturedSessionRecord(
    string Stage,
    string SessionId,
    long Sequence,
    DateTimeOffset Timestamp,
    string? Name,
    object? Payload)
{
    public static CapturedSessionRecord FromRecord(string stage, SessionRecord record)
        => new(
            stage,
            record.SessionId,
            record.Sequence,
            record.Timestamp,
            record.Name,
            record.Payload);
}

internal sealed class SampleCapture
{
    private readonly object _gate = new();
    private readonly List<CapturedSessionRecord> _records = [];

    public void Add(string stage, SessionRecord record)
    {
        lock (_gate)
        {
            _records.Add(CapturedSessionRecord.FromRecord(stage, record));
        }
    }

    public IReadOnlyList<CapturedSessionRecord> GetRecords()
    {
        lock (_gate)
        {
            return [.. _records];
        }
    }
}
