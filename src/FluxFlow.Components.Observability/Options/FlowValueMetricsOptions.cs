namespace FluxFlow.Components.Observability.Options;

public sealed record FlowValueMetricsOptions
{
    public string? Name { get; init; }

    public int BoundedCapacity { get; init; } = 128;

    internal string EffectiveName
        => string.IsNullOrWhiteSpace(Name) ? "metrics" : Name.Trim();
}
