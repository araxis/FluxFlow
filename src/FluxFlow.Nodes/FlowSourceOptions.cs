namespace FluxFlow.Nodes;

/// <summary>
/// Options for <see cref="FlowSource{TOutput}"/> output delivery.
/// </summary>
public sealed record FlowSourceOptions
{
    private int _outputCapacity = 128;

    /// <summary>
    /// Configures the bounded reliable output queue capacity.
    /// </summary>
    public int OutputCapacity
    {
        get => _outputCapacity;
        init
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(OutputCapacity),
                    "OutputCapacity must be greater than zero.");

            _outputCapacity = value;
        }
    }
}
