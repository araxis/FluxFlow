using FluxFlow.Components.Projections.Contracts;

namespace FluxFlow.Components.Projections.Options;

public sealed record EventProjectionOptions
{
    public string? Name { get; init; }
    public EventFilter Filter { get; init; } = new();
    public double RateWindowSeconds { get; init; } = 60;
    public bool EmitEveryMatch { get; init; } = true;
    public bool EmitFinalSnapshot { get; init; }
    public int MaxPreviewChars { get; init; } = 256;
    public int BoundedCapacity { get; init; } = 128;
}
