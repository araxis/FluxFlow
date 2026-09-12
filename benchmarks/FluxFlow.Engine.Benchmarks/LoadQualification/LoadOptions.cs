using System.Globalization;

namespace FluxFlow.Engine.Benchmarks.LoadQualification;

internal sealed record LoadOptions
{
    public bool Updates { get; init; }
    public int Seconds { get; init; } = 10;
    public int Hops { get; init; } = 8;
    public int Fanout { get; init; } = 2;
    public int Capacity { get; init; } = 16;
    public int Producers { get; init; } = 4;
    public int PayloadBytes { get; init; } = 4096;
    public string PayloadKind { get; init; } = "json";
    public string Tracing { get; init; } = "off";
    public bool InputTraceContext { get; init; }
    public int SlowTargetMilliseconds { get; init; }
    public int WarmupMessages { get; init; } = 64;
    public long MessageLimit { get; init; }

    internal int InFlightBound => Producers + Hops * (2 * Capacity + 1) + Fanout * Capacity +
        (Updates ? (Hops * 3 + Fanout) * Capacity : 0);

    internal void Validate()
    {
        if (Tracing is not ("off" or "unsampled" or "propagation" or "recorded"))
            throw new ArgumentException("Tracing must be off, unsampled, propagation or recorded.", nameof(Tracing));
        if (PayloadKind is not ("json" or "binary"))
            throw new ArgumentException("Payload kind must be json or binary.", nameof(PayloadKind));
        ArgumentOutOfRangeException.ThrowIfLessThan(Seconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Seconds, 120);
        ArgumentOutOfRangeException.ThrowIfLessThan(Hops, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Hops, 32);
        ArgumentOutOfRangeException.ThrowIfLessThan(Fanout, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Fanout, 8);
        ArgumentOutOfRangeException.ThrowIfLessThan(Capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Capacity, 4096);
        ArgumentOutOfRangeException.ThrowIfLessThan(Producers, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Producers, 64);
        ArgumentOutOfRangeException.ThrowIfNegative(PayloadBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(PayloadBytes, 4 * 1024 * 1024);
        ArgumentOutOfRangeException.ThrowIfNegative(SlowTargetMilliseconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(SlowTargetMilliseconds, 1000);
        ArgumentOutOfRangeException.ThrowIfNegative(WarmupMessages);
        ArgumentOutOfRangeException.ThrowIfNegative(MessageLimit);
    }

    internal static (LoadOptions Options, string Output) Parse(string[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("Load options require --name value pairs.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
            if (!values.TryAdd(args[index], args[index + 1])) throw new ArgumentException("Duplicate load option.");
        string Take(string name, string fallback) => values.Remove(name, out var value) ? value : fallback;
        int Number(string name, int fallback) => int.Parse(Take(name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        var mode = Take("--runtime", "direct");
        if (mode is not ("direct" or "updates")) throw new ArgumentException("Runtime must be direct or updates.");
        var options = new LoadOptions
        {
            Updates = mode == "updates", Seconds = Number("--seconds", 10), Hops = Number("--hops", 8),
            Fanout = Number("--fanout", 2), Capacity = Number("--capacity", 16), Producers = Number("--producers", 4),
            PayloadBytes = Number("--payload-bytes", 4096), SlowTargetMilliseconds = Number("--slow-target-ms", 0),
            PayloadKind = Take("--payload-kind", "json"),
            Tracing = Take("--tracing", "off"),
            InputTraceContext = bool.Parse(Take("--input-trace-context", "false")),
            MessageLimit = Number("--messages", 0)
        };
        var output = Take("--output", "");
        if (string.IsNullOrWhiteSpace(output) || values.Count != 0) throw new ArgumentException("An output path is required; unknown load options are rejected.");
        options.Validate();
        return (options, output);
    }
}
