namespace FluxFlow.Execution;

internal sealed class ExecutionOtlpBuffer(int capacity = ExecutionOtlpBuffer.Capacity)
{
    public const int Capacity = 1000;
    private readonly object _gate = new();
    private readonly Queue<byte[]> _exports = new();
    private long _sequence;
    private bool _completed;

    // Simple export processors emit one record per export, synchronously before
    // the SDK reuses the record. Store the SDK's owned byte copy unchanged.
    public void Append(byte[] export)
    {
        lock (_gate)
        {
            if (_completed) return;
            _exports.Enqueue(export);
            _sequence++;
            if (_exports.Count > capacity) _exports.Dequeue();
        }
    }

    public (byte[][] Exports, long Dropped) Snapshot()
    {
        lock (_gate) return (_exports.ToArray(), _sequence - _exports.Count);
    }

    public void Complete() { lock (_gate) _completed = true; }
}
