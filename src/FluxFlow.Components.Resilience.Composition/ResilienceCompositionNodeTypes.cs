using FluxFlow.Composition;

namespace FluxFlow.Components.Resilience.Composition;

public static class ResilienceCompositionNodeTypes
{
    public const string Retry = "flow.retry";

    internal static CompositionComponentTypeDescriptor RetryDescriptor { get; } =
        new(Retry, []);
}
