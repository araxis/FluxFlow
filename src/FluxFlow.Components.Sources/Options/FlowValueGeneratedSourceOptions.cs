namespace FluxFlow.Components.Sources.Options;

public sealed record FlowValueGeneratedSourceOptions
{
    public const string DefaultName = "generated";

    public string Name { get; init; } = DefaultName;

    public bool Loop { get; init; }

    public int? MaxItems { get; init; }

    public int InitialDelayMilliseconds { get; init; }

    public int IntervalMilliseconds { get; init; }

    public int BoundedCapacity { get; init; } = 128;

    internal string EffectiveName
        => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name.Trim();
}
