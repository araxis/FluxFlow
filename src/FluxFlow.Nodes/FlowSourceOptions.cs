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
    /// Configures the underlying source broadcast output block capacity. Use
    /// <see cref="UnboundedOutputCapacity"/> for unbounded output. Broadcast output
    /// remains latest-wins; this is not a durable queue or no-loss delivery guarantee.
    /// </summary>
    public int OutputCapacity { get; init; } = UnboundedOutputCapacity;
}
