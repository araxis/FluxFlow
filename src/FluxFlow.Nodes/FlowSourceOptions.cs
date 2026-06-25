namespace FluxFlow.Nodes;

/// <summary>
/// Options for <see cref="FlowSource{TOutput}"/> output delivery.
/// </summary>
public sealed record FlowSourceOptions
{
    /// <summary>
    /// Keeps the source output unbounded. This preserves the original source behavior for
    /// sources that do not opt into an explicit output capacity.
    /// </summary>
    public const int UnboundedOutputCapacity = -1;

    /// <summary>
    /// Maximum number of output messages the source output can hold before
    /// <see cref="FlowSource{TOutput}"/> emission waits. Use
    /// <see cref="UnboundedOutputCapacity"/> for unbounded output.
    /// </summary>
    public int OutputCapacity { get; init; } = UnboundedOutputCapacity;
}
