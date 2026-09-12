namespace FluxFlow.Engine.Benchmarks.LoadQualification;

/// <summary>Fixed storage; percentiles are upper bounds of 64 logarithmic buckets per doubling.</summary>
internal sealed class LatencyHistogram
{
    private const int Precision = 64;
    private readonly long[] _buckets = new long[2048];
    private long _count;

    internal long Count => Volatile.Read(ref _count);

    internal void Record(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var position = Math.Log2(1 + milliseconds * 1000) * Precision;
        if (position >= _buckets.Length) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var index = (int)position;
        Interlocked.Increment(ref _buckets[index]);
        Interlocked.Increment(ref _count);
    }

    internal double UpperBound(double percentile)
    {
        if (!double.IsFinite(percentile) || percentile <= 0 || percentile > 1)
            throw new ArgumentOutOfRangeException(nameof(percentile));
        var count = Count;
        if (count == 0) return 0;
        var rank = (long)Math.Ceiling(count * percentile);
        long cumulative = 0;
        for (var index = 0; index < _buckets.Length; index++)
        {
            cumulative += Volatile.Read(ref _buckets[index]);
            if (cumulative >= rank) return (Math.Pow(2, (index + 1d) / Precision) - 1) / 1000;
        }
        throw new InvalidOperationException("Read latency summaries only after writers have settled.");
    }

    internal LatencySummary Summarize() => new(Count, UpperBound(.50), UpperBound(.95), UpperBound(.99));
}

internal sealed record LatencySummary(long Samples, double P50UpperMilliseconds,
    double P95UpperMilliseconds, double P99UpperMilliseconds);
