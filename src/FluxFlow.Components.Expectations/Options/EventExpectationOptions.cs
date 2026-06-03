using FluxFlow.Components.Projections.Contracts;

namespace FluxFlow.Components.Expectations.Options;

public sealed record EventExpectationOptions
{
    public string? Name { get; init; }
    public EventFilter? Filter { get; init; } = new();
    public double? TimeoutMilliseconds { get; init; }
    public int MaxObservedEvents { get; init; } = 10;
    public int MaxPreviewChars { get; init; } = 256;
    public int BoundedCapacity { get; init; } = 128;
}
