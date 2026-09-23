using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Deduplicate;

/// <summary>Forwards the first occurrence of a selected key within bounded, time-limited memory.</summary>
public sealed class Deduplicate<TKey> : FlowTransformNode where TKey : notnull
{
    private readonly Func<FlowMessage, TKey> _select;
    private readonly HashSet<TKey> _seen;
    private readonly Queue<(TKey Key, long Timestamp)> _order = new();
    private readonly TimeSpan _retention;
    private readonly int _maxKeys;
    private readonly TimeProvider _time;

    public Deduplicate(DeduplicateOptions<TKey> options)
        : base(Validate(options).Execution, options.OutputCapacity)
    {
        _select = options.KeySelector;
        _seen = new HashSet<TKey>(options.Comparer);
        (_retention, _maxKeys, _time) = (options.Retention, options.MaxKeys, options.TimeProvider);
    }

    protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        var key = _select(message);
        ArgumentNullException.ThrowIfNull(key);
        var now = _time.GetTimestamp();
        while (_order.TryPeek(out var oldest) && _time.GetElapsedTime(oldest.Timestamp, now) >= _retention)
            RemoveOldest();
        if (_seen.Contains(key)) return Task.FromResult<IEnumerable<FlowMessage>>([]);
        if (_order.Count == _maxKeys) RemoveOldest();
        _seen.Add(key);
        _order.Enqueue((key, now));
        return Task.FromResult<IEnumerable<FlowMessage>>([message]);
    }

    private void RemoveOldest()
    {
        _seen.Remove(_order.Peek().Key);
        _order.Dequeue();
    }

    protected override ValueTask OnDisposeAsync()
    {
        _seen.Clear();
        _order.Clear();
        return ValueTask.CompletedTask;
    }

    private static DeduplicateOptions<TKey> Validate(DeduplicateOptions<TKey> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.KeySelector);
        ArgumentNullException.ThrowIfNull(options.Comparer);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxKeys);
        if (options.Retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.Retention));
        if (options.Execution is { } execution && (execution.MaxDegreeOfParallelism != 1 || !execution.EnsureOrdered))
            throw new ArgumentException("Deduplicate requires ordered serial processing.", nameof(options));
        return options;
    }
}
