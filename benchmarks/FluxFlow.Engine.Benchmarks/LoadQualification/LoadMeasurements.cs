using System.Collections.Concurrent;
using System.Diagnostics;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Benchmarks.LoadQualification;

internal sealed class LoadMeasurements
{
    private readonly ConcurrentDictionary<MessageId, PendingDelivery> _pending = new();
    private readonly long[] _targets;
    private readonly int _allTargets;
    private readonly int _trackingLimit;
    private int _trackingCount, _peakTracked;
    private long _attempts, _admitted, _canceled, _rejected, _completed;
    internal LatencyHistogram Admission { get; } = new();
    internal LatencyHistogram Completion { get; } = new();
    internal int Pending => Volatile.Read(ref _trackingCount);
    internal int PeakTracked => Volatile.Read(ref _peakTracked);
    internal long Attempts => Volatile.Read(ref _attempts);
    internal long Admitted => Volatile.Read(ref _admitted);
    internal long Canceled => Volatile.Read(ref _canceled);
    internal long Rejected => Volatile.Read(ref _rejected);
    internal long Completed => Volatile.Read(ref _completed);
    internal long[] TargetCounts => _targets.ToArray();

    internal LoadMeasurements(int fanout, int trackingLimit = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fanout, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fanout, 8);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trackingLimit);
        _targets = new long[fanout];
        _allTargets = (1 << fanout) - 1;
        _trackingLimit = trackingLimit;
    }

    internal Task? Register(FlowMessage message, bool warmup = false)
    {
        var item = new PendingDelivery(message, warmup);
        var tracked = Interlocked.Increment(ref _trackingCount);
        if (tracked > _trackingLimit || !_pending.TryAdd(message.MessageId, item))
        {
            Interlocked.Decrement(ref _trackingCount);
            throw new InvalidOperationException("Duplicate producer identity or in-flight tracking bound exceeded.");
        }
        int peak;
        do { peak = Volatile.Read(ref _peakTracked); }
        while (tracked > peak && Interlocked.CompareExchange(ref _peakTracked, tracked, peak) != peak);
        if (!warmup) Interlocked.Increment(ref _attempts);
        return item.WarmupCompletion?.Task;
    }

    internal void Accept(double admissionMilliseconds, bool warmup = false)
    {
        if (warmup) return;
        Interlocked.Increment(ref _admitted);
        Admission.Record(admissionMilliseconds);
    }

    internal void Decline(FlowMessage message, bool canceled)
    {
        if (!_pending.TryGetValue(message.MessageId, out var item) || Volatile.Read(ref item.Received) != 0 ||
            !_pending.TryRemove(message.MessageId, out _))
            throw new InvalidOperationException("A declined message was already delivered.");
        Interlocked.Decrement(ref _trackingCount);
        if (canceled) Interlocked.Increment(ref _canceled);
        else Interlocked.Increment(ref _rejected);
    }

    internal void Receive(int target, FlowMessage message)
    {
        if ((uint)target >= _targets.Length) throw new ArgumentOutOfRangeException(nameof(target));
        if (!_pending.TryGetValue(message.MessageId, out var item)) throw new InvalidOperationException("Unknown or duplicate delivery.");
        if (!ReferenceEquals(item.Message, message)) throw new InvalidOperationException("Fanout changed the original envelope.");
        var bit = 1 << target;
        var before = Interlocked.Or(ref item.Received, bit);
        if ((before & bit) != 0) throw new InvalidOperationException("A target received a message twice.");
        if (item.WarmupCompletion is null) Interlocked.Increment(ref _targets[target]);
        if ((before | bit) != _allTargets) return;
        if (!_pending.TryRemove(message.MessageId, out _)) throw new InvalidOperationException("Delivery tracking disappeared.");
        Interlocked.Decrement(ref _trackingCount);
        if (item.WarmupCompletion is not null) item.WarmupCompletion.TrySetResult();
        else
        {
            Completion.Record(Stopwatch.GetElapsedTime(item.Started).TotalMilliseconds);
            Interlocked.Increment(ref _completed);
        }
    }

    internal void ValidateDrained()
    {
        if (Pending != 0 || Attempts != Admitted + Canceled + Rejected || Admitted != Completed || Rejected != 0 ||
            _targets.Any(count => count != Completed) || Admission.Count != Admitted || Completion.Count != Completed)
            throw new InvalidOperationException("Load accounting did not settle with exact reliable delivery.");
    }

    private sealed class PendingDelivery(FlowMessage message, bool warmup)
    {
        internal readonly FlowMessage Message = message;
        internal readonly long Started = Stopwatch.GetTimestamp();
        internal readonly TaskCompletionSource? WarmupCompletion = warmup
            ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        internal int Received;
    }
}
