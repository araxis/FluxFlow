using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Projections.Contracts;

namespace FluxFlow.Components.Expectations.Options;

/// <summary>
/// Settings for an <see cref="EventExpectationNode"/>. A plain record — no engine,
/// no JSON reader. <see cref="Kind"/> selects expect-vs-guard semantics;
/// <see cref="Filter"/> is the neutral <see cref="EventFilter"/> shared with the
/// Projections package; <see cref="TimeoutMilliseconds"/> arms a deterministic
/// timeout over the injected <see cref="TimeProvider"/> when set.
/// </summary>
public sealed record EventExpectationOptions
{
    public EventExpectationNodeKind Kind { get; init; } = EventExpectationNodeKind.Expect;
    public string? Name { get; init; }
    public EventFilter? Filter { get; init; } = new();
    public double? TimeoutMilliseconds { get; init; }
    public int MaxObservedEvents { get; init; } = 10;
    public int MaxPreviewChars { get; init; } = 256;
    public int BoundedCapacity { get; init; } = 128;
}
