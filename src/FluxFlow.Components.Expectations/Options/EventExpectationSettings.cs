using FluxFlow.Components.Projections.Contracts;

namespace FluxFlow.Components.Expectations.Options;

internal sealed record EventExpectationSettings
{
    public string? Name { get; init; }
    public EventFilter Filter { get; init; } = new();
    public TimeSpan? Timeout { get; init; }
    public required int MaxObservedEvents { get; init; }
    public required int MaxPreviewChars { get; init; }
    public required int BoundedCapacity { get; init; }
}
