namespace FluxFlow.Components.Sources.Options;

public sealed record SequenceSourceOptions
{
    public const string DefaultName = "sequence";

    public string Name { get; init; } = DefaultName;
    public long Start { get; init; } = 1;
    public long Step { get; init; } = 1;
    public int Count { get; init; } = 1;
    public int InitialDelayMilliseconds { get; init; }
    public int IntervalMilliseconds { get; init; }
    public int BoundedCapacity { get; init; } = 128;

    internal string EffectiveName
        => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name.Trim();
}
