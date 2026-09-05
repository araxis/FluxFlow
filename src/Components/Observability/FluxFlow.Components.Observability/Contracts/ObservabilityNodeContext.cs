namespace FluxFlow.Components.Observability.Contracts;

/// <summary>
/// The context handed to value selectors and context factories when an
/// observability node reads a value from an input. Carries the node's role
/// (<see cref="NodeType"/>), the configured <see cref="InputType"/>, and the
/// node's <see cref="Name"/> — no engine, runtime, or address dependency.
/// </summary>
public sealed record ObservabilityNodeContext
{
    public required string NodeType { get; init; }
    public required Type InputType { get; init; }
    public required string Name { get; init; }
}
