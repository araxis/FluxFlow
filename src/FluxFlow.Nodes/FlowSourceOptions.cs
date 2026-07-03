namespace FluxFlow.Nodes;

/// <summary>
/// Options for <see cref="FlowSource{TOutput}"/> output delivery.
/// </summary>
public sealed record FlowSourceOptions
{
    private int _outputCapacity = UnboundedOutputCapacity;

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
    public int OutputCapacity
    {
        get => _outputCapacity;
        init
        {
            if (value != UnboundedOutputCapacity && value <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(OutputCapacity),
                    "OutputCapacity must be greater than zero or unbounded.");

            _outputCapacity = value;
        }
    }
}
